#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class BB_RSI_EMA50_Strategy : Strategy
    {
        private Bollinger bollinger;
        private RSI rsi;
        private EMA ema;

        private const string LongSignal = "BBRSI_Long";
        private const string ShortSignal = "BBRSI_Short";

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BollingerPeriod", Order = 1, GroupName = "Parameters")]
        public int BollingerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "BollingerStdDev", Order = 2, GroupName = "Parameters")]
        public double BollingerStdDev { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "RSIPeriod", Order = 3, GroupName = "Parameters")]
        public int RSIPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "RSISmooth", Order = 4, GroupName = "Parameters")]
        public int RSISmooth { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMAPeriod", Order = 5, GroupName = "Parameters")]
        public int EMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TrendLookbackBars", Order = 6, GroupName = "Parameters")]
        public int TrendLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Order = 7, GroupName = "Parameters")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "StopLossTicks", Order = 8, GroupName = "Risk")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ProfitTargetTicks", Order = 9, GroupName = "Risk")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "UseTrailingStop", Order = 10, GroupName = "Risk")]
        public bool UseTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TrailingTriggerTicks", Order = 11, GroupName = "Risk")]
        public int TrailingTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.95)]
        [Display(Name = "TrailingLockPercent", Order = 12, GroupName = "Risk")]
        public double TrailingLockPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MaxBarsInTrade", Order = 13, GroupName = "Risk")]
        public int MaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AllowLongs", Order = 14, GroupName = "Trade Direction")]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AllowShorts", Order = 15, GroupName = "Trade Direction")]
        public bool AllowShorts { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BB_RSI_EMA50_Strategy";
                Description = "Combines Bollinger Bands, RSI and EMA(50) trend filter.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                BollingerPeriod = 20;
                BollingerStdDev = 2.0;
                RSIPeriod = 14;
                RSISmooth = 3;
                EMAPeriod = 50;
                TrendLookbackBars = 5;
                Quantity = 1;
                StopLossTicks = 100;
                ProfitTargetTicks = 200;
                UseTrailingStop = true;
                TrailingTriggerTicks = 150;
                TrailingLockPercent = 0.50;
                MaxBarsInTrade = 30;
                AllowLongs = true;
                AllowShorts = true;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(LongSignal, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(LongSignal, CalculationMode.Ticks, ProfitTargetTicks);
                SetStopLoss(ShortSignal, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(ShortSignal, CalculationMode.Ticks, ProfitTargetTicks);
            }
            else if (State == State.DataLoaded)
            {
                bollinger = Bollinger(BollingerStdDev, BollingerPeriod);
                rsi = RSI(RSIPeriod, RSISmooth);
                ema = EMA(EMAPeriod);

                AddChartIndicator(bollinger);
                AddChartIndicator(rsi);
                AddChartIndicator(ema);
            }
        }

        protected override void OnBarUpdate()
        {
            int minBars = Math.Max(Math.Max(BollingerPeriod, RSIPeriod), EMAPeriod) + TrendLookbackBars;
            if (CurrentBar < minBars)
                return;

            ManageOpenPosition();

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            ResetManagedStops();

            bool emaUp = ema[0] > ema[TrendLookbackBars];
            bool emaDown = ema[0] < ema[TrendLookbackBars];

            bool priceTouchesLowerBand = Low[0] <= bollinger.Lower[0];
            bool priceTouchesUpperBand = High[0] >= bollinger.Upper[0];

            bool rsiBuyConfirm = CrossAbove(rsi, 30, 1) || rsi[0] <= 30;
            bool rsiSellConfirm = CrossBelow(rsi, 70, 1) || rsi[0] >= 70;

            if (AllowLongs && emaUp && priceTouchesLowerBand && rsiBuyConfirm)
                EnterLong(Quantity, LongSignal);

            if (AllowShorts && emaDown && priceTouchesUpperBand && rsiSellConfirm)
                EnterShort(Quantity, ShortSignal);
        }

        private void ManageOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (BarsSinceEntryExecution(0, LongSignal, 0) >= MaxBarsInTrade)
                    ExitLong("TimeExit_Long", LongSignal);

                if (UseTrailingStop)
                {
                    double unrealizedTicks = (Close[0] - Position.AveragePrice) / TickSize;
                    if (unrealizedTicks >= TrailingTriggerTicks)
                    {
                        double lockedTicks = Math.Floor(unrealizedTicks * TrailingLockPercent);
                        double stopPrice = Position.AveragePrice + lockedTicks * TickSize;
                        SetStopLoss(LongSignal, CalculationMode.Price, stopPrice, false);
                    }
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (BarsSinceEntryExecution(0, ShortSignal, 0) >= MaxBarsInTrade)
                    ExitShort("TimeExit_Short", ShortSignal);

                if (UseTrailingStop)
                {
                    double unrealizedTicks = (Position.AveragePrice - Close[0]) / TickSize;
                    if (unrealizedTicks >= TrailingTriggerTicks)
                    {
                        double lockedTicks = Math.Floor(unrealizedTicks * TrailingLockPercent);
                        double stopPrice = Position.AveragePrice - lockedTicks * TickSize;
                        SetStopLoss(ShortSignal, CalculationMode.Price, stopPrice, false);
                    }
                }
            }
        }

        private void ResetManagedStops()
        {
            SetStopLoss(LongSignal, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(LongSignal, CalculationMode.Ticks, ProfitTargetTicks);
            SetStopLoss(ShortSignal, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(ShortSignal, CalculationMode.Ticks, ProfitTargetTicks);
        }
    }
}
