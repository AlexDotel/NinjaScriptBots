#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class FVGPullback : Strategy
    {
        private const string EntrySignal = "FVG Pullback Long";

        private TimeZoneInfo easternTimeZone;
        private Order entryOrder;
        private bool setupActive;
        private bool pullbackTouched;
        private int setupAgeBars;
        private DateTime activeEasternDate;

        private double entryPrice;
        private double pullbackPrice;
        private double stopPrice;
        private double targetPrice;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                                    = "FVG Pullback";
                Description                             = "Bullish three-candle FVG pullback: arm at 61%, buy stop at 50%, Fibonacci stop and fixed-dollar risk.";
                Calculate                               = Calculate.OnEachTick;
                EntriesPerDirection                     = 1;
                EntryHandling                           = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy            = false;
                ExitOnSessionCloseSeconds               = 30;
                IsFillLimitOnTouch                      = false;
                MaximumBarsLookBack                     = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                     = OrderFillResolution.Standard;
                Slippage                                = 0;
                StartBehavior                           = StartBehavior.WaitUntilFlat;
                TimeInForce                             = TimeInForce.Gtc;
                TraceOrders                             = false;
                RealtimeErrorHandling                   = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                      = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                     = 4;
                IsInstantiatedOnEachOptimizationIteration = true;

                RiskDollars         = 300;
                MinimumFvgPercent   = 20;
                PullbackPercent     = 61;
                EntryPercent        = 50;
                StopFibPercent      = 61;
                RewardRiskMultiple  = 2;
                SetupExpiryBars     = 10;
                MaxQuantity         = 100;
                StartTimeEastern    = DateTime.Today.AddHours(9).AddMinutes(30);
                EndTimeEastern      = DateTime.Today.AddHours(16);
                ExitPositionAtEnd   = false;
                EnableDiagnostics   = false;
            }
            else if (State == State.DataLoaded)
            {
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                ResetSetup(false);
                activeEasternDate = DateTime.MinValue;
            }
            else if (State == State.Terminated)
            {
                entryOrder = null;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
                return;

            DateTime easternBarTime = ToEastern(Time[0]);

            if (activeEasternDate != easternBarTime.Date)
            {
                activeEasternDate = easternBarTime.Date;
                CancelWorkingEntry();
                ResetSetup(false);
            }

            bool insideSession = IsInsideEasternSession(easternBarTime.TimeOfDay);
            if (!insideSession)
            {
                CancelWorkingEntry();
                ResetSetup(false);

                if (ExitPositionAtEnd && Position.MarketPosition == MarketPosition.Long)
                    ExitLong("Session close", EntrySignal);

                return;
            }

            // Pattern detection and setup age are evaluated only once per bar.
            // With Calculate.OnEachTick, shifts 1/2/3 are fully closed candles.
            if (IsFirstTickOfBar)
            {
                if (setupActive)
                {
                    setupAgeBars++;
                    if (setupAgeBars >= SetupExpiryBars)
                    {
                        CancelWorkingEntry();
                        ResetSetup(false);
                    }
                }

                if (!setupActive
                    && Position.MarketPosition == MarketPosition.Flat
                    && !IsEntryWorking())
                {
                    DetectBullishFvg();
                }
            }

            if (!setupActive || Position.MarketPosition != MarketPosition.Flat || IsEntryWorking())
                return;

            // The pullback must happen after the three-candle pattern has closed.
            if (!pullbackTouched && Low[0] <= pullbackPrice)
            {
                pullbackTouched = true;

                int quantity = CalculateRiskQuantity(entryPrice, stopPrice);
                double currentAsk = State == State.Historical ? Close[0] : GetCurrentAsk();

                // A buy stop must still be above the market. If the same update has
                // already crossed 50%, do not invent a retrospective fill.
                if (quantity >= 1 && entryPrice > currentAsk && entryPrice > stopPrice)
                {
                    SetStopLoss(EntrySignal, CalculationMode.Price, stopPrice, false);
                    SetProfitTarget(EntrySignal, CalculationMode.Price, targetPrice);
                    entryOrder = EnterLongStopMarket(0, true, quantity, entryPrice, EntrySignal);

                    if (EnableDiagnostics)
                        Print(string.Format("{0} ET | Armed. Qty={1}, Entry={2}, SL={3}, TP={4}",
                            easternBarTime, quantity, entryPrice, stopPrice, targetPrice));
                }
                else
                {
                    if (EnableDiagnostics)
                        Print(string.Format("{0} ET | Setup skipped. Qty={1}, entry={2}, ask/close={3}, stop={4}",
                            easternBarTime, quantity, entryPrice, currentAsk, stopPrice));
                    ResetSetup(false);
                }
            }
        }

        private void DetectBullishFvg()
        {
            // Candle 1 (oldest) = [3], candle 2 = [2], candle 3 (newest) = [1].
            double impulse = High[1] - Low[3];
            double fvgSize = Low[1] - High[3];

            if (impulse <= 0 || fvgSize <= 0)
                return;

            if (fvgSize + TickSize * 0.0001 < impulse * MinimumFvgPercent / 100.0)
                return;

            double rawEntry    = High[1] - impulse * EntryPercent / 100.0;
            double rawPullback = High[1] - impulse * PullbackPercent / 100.0;
            double rawStop     = High[1] - impulse * StopFibPercent / 100.0;

            entryPrice    = Instrument.MasterInstrument.RoundToTickSize(rawEntry);
            pullbackPrice = Instrument.MasterInstrument.RoundToTickSize(rawPullback);
            stopPrice     = Instrument.MasterInstrument.RoundToTickSize(rawStop);

            if (entryPrice <= stopPrice || pullbackPrice >= entryPrice)
                return;

            double riskDistance = entryPrice - stopPrice;
            targetPrice = Instrument.MasterInstrument.RoundToTickSize(
                entryPrice + RewardRiskMultiple * riskDistance);

            if (targetPrice <= entryPrice)
                return;

            setupActive     = true;
            pullbackTouched = false;
            setupAgeBars    = 0;

            if (EnableDiagnostics)
                Print(string.Format("{0} | Bullish FVG. Impulse={1}, gap={2}, entry={3}, pullback={4}, SL={5}, TP={6}",
                    Time[0], impulse, fvgSize, entryPrice, pullbackPrice, stopPrice, targetPrice));
        }

        private int CalculateRiskQuantity(double plannedEntry, double plannedStop)
        {
            double pointValue = Instrument.MasterInstrument.PointValue;
            double riskPerUnit = Math.Abs(plannedEntry - plannedStop) * pointValue;

            if (riskPerUnit <= 0 || RiskDollars <= 0)
                return 0;

            // Always round down so planned price risk never exceeds RiskDollars.
            int quantity = (int)Math.Floor((RiskDollars + 1e-9) / riskPerUnit);
            return Math.Max(0, Math.Min(quantity, MaxQuantity));
        }

        private DateTime ToEastern(DateTime platformTime)
        {
            DateTime unspecified = DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTime(
                unspecified,
                Core.Globals.GeneralOptions.TimeZoneInfo,
                easternTimeZone);
        }

        private bool IsInsideEasternSession(TimeSpan time)
        {
            TimeSpan start = StartTimeEastern.TimeOfDay;
            TimeSpan end   = EndTimeEastern.TimeOfDay;

            if (start < end)
                return time >= start && time < end;

            // Also supports an optional session crossing midnight.
            return time >= start || time < end;
        }

        private bool IsEntryWorking()
        {
            return entryOrder != null
                && (entryOrder.OrderState == OrderState.Accepted
                    || entryOrder.OrderState == OrderState.Submitted
                    || entryOrder.OrderState == OrderState.Working
                    || entryOrder.OrderState == OrderState.PartFilled);
        }

        private void CancelWorkingEntry()
        {
            if (IsEntryWorking())
                CancelOrder(entryOrder);
        }

        private void ResetSetup(bool keepOrderReference)
        {
            setupActive     = false;
            pullbackTouched = false;
            setupAgeBars    = 0;
            entryPrice      = 0;
            pullbackPrice   = 0;
            stopPrice       = 0;
            targetPrice     = 0;

            if (!keepOrderReference && !IsEntryWorking())
                entryOrder = null;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPriceUpdate,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string nativeError)
        {
            if (order.Name != EntrySignal)
                return;

            entryOrder = order;

            if (orderState == OrderState.Filled)
            {
                ResetSetup(true);
            }
            else if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
            {
                if (EnableDiagnostics && orderState == OrderState.Rejected)
                    Print(string.Format("Entry rejected: {0} / {1}", error, nativeError));

                entryOrder = null;
                ResetSetup(false);
            }
        }

        #region Parameters
        [NinjaScriptProperty]
        [Range(1, double.MaxValue)]
        [Display(Name = "Risk dollars", GroupName = "Risk", Order = 0)]
        public double RiskDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Maximum quantity", GroupName = "Risk", Order = 1)]
        public int MaxQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100)]
        [Display(Name = "Minimum FVG percent", GroupName = "FVG", Order = 0)]
        public double MinimumFvgPercent { get; set; }

        [NinjaScriptProperty]
        [Range(50.01, 1000)]
        [Display(Name = "Pullback percent", GroupName = "Fibonacci", Order = 0)]
        public double PullbackPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 99.99)]
        [Display(Name = "Entry percent", GroupName = "Fibonacci", Order = 1)]
        public double EntryPercent { get; set; }

        [NinjaScriptProperty]
        [Range(55.01, 1000)]
        [Display(Name = "Stop Fibonacci percent", GroupName = "Fibonacci", Order = 2)]
        public double StopFibPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "Reward/risk multiple", GroupName = "Risk", Order = 2)]
        public double RewardRiskMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Setup expiry bars", GroupName = "FVG", Order = 1)]
        public int SetupExpiryBars { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start time (New York)", GroupName = "Session", Order = 0)]
        public DateTime StartTimeEastern { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End time (New York)", GroupName = "Session", Order = 1)]
        public DateTime EndTimeEastern { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit position at session end", GroupName = "Session", Order = 2)]
        public bool ExitPositionAtEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable diagnostics", GroupName = "Diagnostics", Order = 0)]
        public bool EnableDiagnostics { get; set; }
        #endregion
    }
}
