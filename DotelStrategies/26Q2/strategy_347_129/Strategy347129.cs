#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Strategy347129 : Strategy
    {
        private const string LongEntrySignalName = "SQ347129Long";

        private ATR atr;
        private PriorDayOHLC priorDayOhlc;
        private double longTrailingStopPrice;
        private bool entrySubmittedThisBar;
        private int tradesToday;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Recreacion para NinjaTrader 8 de StrategyQuant X Strategy 3.47.129.";
                Name = "Strategy347129";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                BarsRequiredToTrade = 55;
                DefaultQuantity = 1;
                TraceOrders = false;

                MagicNumber = 11111;
                PriceEntryMult1 = 0.9;
                ExitAfterBars1 = 10;
                StopLoss1 = 35;
                TrailingStopCoef1 = 3.9;

                DontTradeOnWeekends = true;
                FridayCloseTime = 1630;
                SundayOpenTime = 1800;
                ExitAtEndOfDay = true;
                DayExitTime = 1630;
                ExitOnFriday = true;
                FridayExitTime = 1630;
                LimitSignalsTimeRange = false;
                SignalTimeRangeFrom = 800;
                SignalTimeRangeTo = 1600;
                ExitAtEndOfRange = false;
                LimitMaxDistanceFromMarket = false;
                MaxDistancePct = 6.0;
                MaxTradesPerDay = 0;
                MinimumSL = 0;
                MaximumSL = 0;
                MinimumPT = 0;
                MaximumPT = 0;
                UseInitialStopLoss = false;

                OrderQuantity = 1;
                MmMultiplier = 1.0;
                InitialCapital = 10000;
            }
            else if (State == State.Configure)
            {
                ValidateTimeInput(FridayCloseTime, nameof(FridayCloseTime));
                ValidateTimeInput(SundayOpenTime, nameof(SundayOpenTime));
                ValidateTimeInput(DayExitTime, nameof(DayExitTime));
                ValidateTimeInput(FridayExitTime, nameof(FridayExitTime));
                ValidateTimeInput(SignalTimeRangeFrom, nameof(SignalTimeRangeFrom));
                ValidateTimeInput(SignalTimeRangeTo, nameof(SignalTimeRangeTo));
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(50);
                priorDayOhlc = PriorDayOHLC();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            entrySubmittedThisBar = false;
            if (Bars.IsFirstBarOfSession)
                tradesToday = 0;

            bool openOrdersAllowed = IsTradingAllowedByOptions();

            if (Position.MarketPosition == MarketPosition.Long)
                ManageLongExits();
            else
                longTrailingStopPrice = 0;

            bool longEntrySignal = Volume[2] > Volume[1];
            bool shortEntrySignal = false;
            bool longExitSignal = false;
            bool shortExitSignal = false;

            if (longEntrySignal)
                TrySubmitLongEntry(openOrdersAllowed);

            if (shortEntrySignal && !longEntrySignal)
            {
                // El codigo original no contiene acciones para entradas cortas.
            }

            if (longExitSignal && !longEntrySignal && Position.MarketPosition == MarketPosition.Long)
                ExitLong("ClosePositionLong", LongEntrySignalName);

            if (shortExitSignal && !shortEntrySignal && Position.MarketPosition == MarketPosition.Short)
                ExitShort("ClosePositionShort");
        }

        private bool IsTradingAllowedByOptions()
        {
            bool allowed = true;
            int timeNow = ToTime(Time[0]) / 100;
            DayOfWeek day = Time[0].DayOfWeek;

            if (DontTradeOnWeekends)
            {
                if (day == DayOfWeek.Friday && FridayCloseTime != 0 && timeNow >= FridayCloseTime)
                    allowed = false;
                else if (day == DayOfWeek.Saturday)
                    allowed = false;
                else if (day == DayOfWeek.Sunday && timeNow < SundayOpenTime)
                    allowed = false;
            }

            if (ExitAtEndOfDay && DayExitTime != 0)
            {
                if (!LimitSignalsTimeRange && timeNow >= DayExitTime)
                {
                    ExitOpenPosition("ExitEndOfDay");
                    allowed = false;
                }
                else if (LimitSignalsTimeRange)
                {
                    if (timeNow >= DayExitTime && timeNow < SignalTimeRangeFrom)
                    {
                        ExitOpenPosition("ExitEndOfDay");
                        allowed = false;
                    }

                    if (DayExitTime >= SignalTimeRangeFrom && SignalTimeRangeFrom < SignalTimeRangeTo
                        && timeNow >= DayExitTime && timeNow >= SignalTimeRangeFrom)
                    {
                        ExitOpenPosition("ExitEndOfDay");
                        allowed = false;
                    }
                }
            }

            if (ExitOnFriday)
            {
                if (day == DayOfWeek.Friday && FridayExitTime != 0 && timeNow >= FridayExitTime)
                {
                    ExitOpenPosition("ExitFriday");
                    allowed = false;
                }
                else if (day == DayOfWeek.Saturday)
                    allowed = false;
            }

            if (allowed && LimitSignalsTimeRange && !IsWithinSignalRange(timeNow))
                allowed = false;

            if (LimitSignalsTimeRange && ExitAtEndOfRange && IsAtEndOfSignalRange(timeNow))
                ExitOpenPosition("ExitEndOfRange");

            if (allowed && MaxTradesPerDay != 0 && tradesToday >= MaxTradesPerDay)
                allowed = false;

            return allowed;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState == OrderState.Filled && execution.Order.Name == LongEntrySignalName)
                tradesToday++;
        }

        private void TrySubmitLongEntry(bool openOrdersAllowed)
        {
            if (!openOrdersAllowed || Position.MarketPosition != MarketPosition.Flat || entrySubmittedThisBar)
                return;

            double previousDayHigh = priorDayOhlc.PriorHigh[0];
            if (previousDayHigh <= 0)
                return;

            double barRangeTwoBarsAgo = High[2] - Low[2];
            double stopPrice = Instrument.MasterInstrument.RoundToTickSize(previousDayHigh + (PriceEntryMult1 * barRangeTwoBarsAgo));

            if (LimitMaxDistanceFromMarket)
            {
                double referencePrice = Close[0];
                if (referencePrice <= 0)
                    return;

                double distancePct = Math.Abs(stopPrice - referencePrice) / referencePrice * 100.0;
                if (Math.Round(distancePct, 2) > MaxDistancePct)
                    return;
            }

            int quantity = Math.Max(0, (int)Math.Round(OrderQuantity * MmMultiplier));
            if (quantity <= 0)
                return;

            if (UseInitialStopLoss)
                SetStopLoss(LongEntrySignalName, CalculationMode.Ticks, ClampTicks(StopLoss1, MinimumSL, MaximumSL), false);

            EnterLongStopMarket(quantity, stopPrice, LongEntrySignalName);
            entrySubmittedThisBar = true;
        }

        private void ManageLongExits()
        {
            int barsSinceEntry = BarsSinceEntryExecution(0, LongEntrySignalName, 0);
            if (barsSinceEntry == 0)
                longTrailingStopPrice = 0;

            double stopLossPrice = Position.AveragePrice - (ClampTicks(StopLoss1, MinimumSL, MaximumSL) * TickSize);
            stopLossPrice = Instrument.MasterInstrument.RoundToTickSize(stopLossPrice);

            double effectiveStopPrice = stopLossPrice;
            double atrDistance = TrailingStopCoef1 * atr[1];

            if (atrDistance > 0 && Close[0] >= Position.AveragePrice)
            {
                double candidateTrailingStop = Instrument.MasterInstrument.RoundToTickSize(Close[0] - atrDistance);
                if ((longTrailingStopPrice == 0 || candidateTrailingStop > longTrailingStopPrice) && candidateTrailingStop < Close[0])
                    longTrailingStopPrice = candidateTrailingStop;
            }

            if (longTrailingStopPrice > 0 && longTrailingStopPrice > effectiveStopPrice)
                effectiveStopPrice = longTrailingStopPrice;

            if (effectiveStopPrice > 0 && effectiveStopPrice < Close[0])
                ExitLongStopMarket(0, true, Position.Quantity, effectiveStopPrice, "LongProtectiveStop", LongEntrySignalName);

            if (ExitAfterBars1 > 0 && barsSinceEntry >= ExitAfterBars1)
                ExitLong("LongExitAfterXBars", LongEntrySignalName);
        }

        private void ExitOpenPosition(string signalPrefix)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(signalPrefix + "L", LongEntrySignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(signalPrefix + "S");
        }

        private bool IsWithinSignalRange(int timeNow)
        {
            if (SignalTimeRangeFrom < SignalTimeRangeTo)
                return timeNow >= SignalTimeRangeFrom && timeNow < SignalTimeRangeTo;

            return timeNow >= SignalTimeRangeFrom || timeNow < SignalTimeRangeTo;
        }

        private bool IsAtEndOfSignalRange(int timeNow)
        {
            if (SignalTimeRangeFrom < SignalTimeRangeTo)
                return timeNow >= SignalTimeRangeTo;

            return timeNow >= SignalTimeRangeTo && timeNow < SignalTimeRangeFrom;
        }

        private int ClampTicks(int ticks, int minimumTicks, int maximumTicks)
        {
            int result = Math.Max(0, ticks);

            if (minimumTicks > 0 && result < minimumTicks)
                result = minimumTicks;

            if (maximumTicks > 0 && result > maximumTicks)
                result = maximumTicks;

            return result;
        }

        private void ValidateTimeInput(int value, string parameterName)
        {
            int hours = value / 100;
            int minutes = value % 100;

            if (value < 0 || hours > 23 || minutes > 59)
                throw new ArgumentOutOfRangeException(parameterName, parameterName + " debe estar en formato HHmm.");
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Magic number", GroupName = "01. Strategy", Order = 0)]
        public int MagicNumber { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Price entry mult", GroupName = "01. Strategy", Order = 1)]
        public double PriceEntryMult1 { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Exit after bars", GroupName = "01. Strategy", Order = 2)]
        public int ExitAfterBars1 { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Stop loss ticks", GroupName = "01. Strategy", Order = 3)]
        public int StopLoss1 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Trailing stop ATR coef", GroupName = "01. Strategy", Order = 4)]
        public double TrailingStopCoef1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dont trade weekends", GroupName = "02. Trading options", Order = 0)]
        public bool DontTradeOnWeekends { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Friday close time", GroupName = "02. Trading options", Order = 1)]
        public int FridayCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Sunday open time", GroupName = "02. Trading options", Order = 2)]
        public int SundayOpenTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit end of day", GroupName = "02. Trading options", Order = 3)]
        public bool ExitAtEndOfDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Day exit time", GroupName = "02. Trading options", Order = 4)]
        public int DayExitTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit on Friday", GroupName = "02. Trading options", Order = 5)]
        public bool ExitOnFriday { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Friday exit time", GroupName = "02. Trading options", Order = 6)]
        public int FridayExitTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit signals time range", GroupName = "02. Trading options", Order = 7)]
        public bool LimitSignalsTimeRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Signal time from", GroupName = "02. Trading options", Order = 8)]
        public int SignalTimeRangeFrom { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Signal time to", GroupName = "02. Trading options", Order = 9)]
        public int SignalTimeRangeTo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit at end of range", GroupName = "02. Trading options", Order = 10)]
        public bool ExitAtEndOfRange { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit max distance", GroupName = "02. Trading options", Order = 11)]
        public bool LimitMaxDistanceFromMarket { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Max distance pct", GroupName = "02. Trading options", Order = 12)]
        public double MaxDistancePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Max trades per day", GroupName = "02. Trading options", Order = 13)]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Minimum SL ticks", GroupName = "02. Trading options", Order = 14)]
        public int MinimumSL { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Maximum SL ticks", GroupName = "02. Trading options", Order = 15)]
        public int MaximumSL { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Minimum PT ticks", GroupName = "02. Trading options", Order = 16)]
        public int MinimumPT { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Maximum PT ticks", GroupName = "02. Trading options", Order = 17)]
        public int MaximumPT { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use initial stop loss", GroupName = "02. Trading options", Order = 18)]
        public bool UseInitialStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Order quantity", GroupName = "03. Money management", Order = 0)]
        public int OrderQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "MM multiplier", GroupName = "03. Money management", Order = 1)]
        public double MmMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Initial capital", GroupName = "03. Money management", Order = 2)]
        public double InitialCapital { get; set; }
    }
}
