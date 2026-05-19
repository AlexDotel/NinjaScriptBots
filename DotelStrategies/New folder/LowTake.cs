#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class LowTake : Strategy
    {
        private const string LongSignalName = "LowTakeLong";

        private ATR atr;
        private PriorDayOHLC priorDay;
        private Series<double> finalUpperBand;
        private Series<double> finalLowerBand;
        private Series<double> superTrendSeries;
        private Series<int> trendDirection;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Compra un rebote sobre el low del dia anterior. Incluye SL/TP fijos y salida opcional por SuperTrend al cierre.";
                Name = "LowTake";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                BarsRequiredToTrade = 30;
                DefaultQuantity = 1;
                TraceOrders = false;

                StopLossTicks = 50;
                ProfitTargetTicks = 50;
                UseSuperTrendTrailing = false;
                SuperTrendAtrPeriod = 28;
                SuperTrendMultiplier = 13.0;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();
                BarsRequiredToTrade = Math.Max(20, SuperTrendAtrPeriod + 2);
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(SuperTrendAtrPeriod);
                priorDay = PriorDayOHLC();

                finalUpperBand = new Series<double>(this);
                finalLowerBand = new Series<double>(this);
                superTrendSeries = new Series<double>(this);
                trendDirection = new Series<int>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            UpdateSuperTrend();

            if (CurrentBar < BarsRequiredToTrade)
                return;

            double priorLow = priorDay != null ? priorDay.PriorLow[0] : double.NaN;
            if (!IsValidPriceLevel(priorLow))
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (UseSuperTrendTrailing && IsValidPriceLevel(superTrendSeries[0]) && Close[0] < superTrendSeries[0])
                    ExitLong("SuperTrendExit", LongSignalName);

                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            bool reboundSignal = Low[0] <= priorLow && Close[0] > priorLow;
            if (!reboundSignal)
                return;

            PrepareProtectiveOrders();
            EnterLong(LongSignalName);
        }

        private void UpdateSuperTrend()
        {
            double midPrice = (High[0] + Low[0]) * 0.5;
            double atrValue = atr[0];

            if (CurrentBar == 0 || atrValue <= 0 || double.IsNaN(atrValue))
            {
                finalUpperBand[0] = midPrice;
                finalLowerBand[0] = midPrice;
                trendDirection[0] = 1;
                superTrendSeries[0] = midPrice;
                return;
            }

            double basicUpperBand = midPrice + (SuperTrendMultiplier * atrValue);
            double basicLowerBand = midPrice - (SuperTrendMultiplier * atrValue);

            finalUpperBand[0] = basicUpperBand < finalUpperBand[1] || Close[1] > finalUpperBand[1]
                ? basicUpperBand
                : finalUpperBand[1];

            finalLowerBand[0] = basicLowerBand > finalLowerBand[1] || Close[1] < finalLowerBand[1]
                ? basicLowerBand
                : finalLowerBand[1];

            int direction = trendDirection[1] == 0 ? 1 : trendDirection[1];

            if (direction == 1 && Close[0] < finalLowerBand[0])
                direction = -1;
            else if (direction == -1 && Close[0] > finalUpperBand[0])
                direction = 1;

            trendDirection[0] = direction;
            superTrendSeries[0] = direction == 1 ? finalLowerBand[0] : finalUpperBand[0];
        }

        private void PrepareProtectiveOrders()
        {
            SetStopLoss(LongSignalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(LongSignalName, CalculationMode.Ticks, ProfitTargetTicks);
        }

        private void ValidateConfiguration()
        {
            if (StopLossTicks <= 0)
                throw new ArgumentOutOfRangeException("StopLossTicks", "StopLossTicks debe ser mayor que 0.");

            if (ProfitTargetTicks <= 0)
                throw new ArgumentOutOfRangeException("ProfitTargetTicks", "ProfitTargetTicks debe ser mayor que 0.");

            if (SuperTrendAtrPeriod <= 0)
                throw new ArgumentOutOfRangeException("SuperTrendAtrPeriod", "SuperTrendAtrPeriod debe ser mayor que 0.");

            if (SuperTrendMultiplier <= 0)
                throw new ArgumentOutOfRangeException("SuperTrendMultiplier", "SuperTrendMultiplier debe ser mayor que 0.");
        }

        private bool IsValidPriceLevel(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit (ticks)", GroupName = "Riesgo", Order = 1)]
        public int ProfitTargetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar trailing SuperTrend", GroupName = "SuperTrend", Order = 0)]
        public bool UseSuperTrendTrailing
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 2000)]
        [Display(Name = "ATR Period", GroupName = "SuperTrend", Order = 1)]
        public int SuperTrendAtrPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 1000.0)]
        [Display(Name = "Multiplier", GroupName = "SuperTrend", Order = 2)]
        public double SuperTrendMultiplier
        { get; set; }
    }
}
