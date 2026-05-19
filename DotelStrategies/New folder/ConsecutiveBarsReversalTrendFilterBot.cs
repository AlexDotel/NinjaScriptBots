#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ConsecutiveBarsReversalTrendFilterBot : Strategy
    {
        private const string LongSignalName = "SeqLong";
        private const string ShortSignalName = "SeqShort";

        private bool breakEvenMoved;
        private int startMinutes;
        private int endMinutes;
        private ADX adx;
        private EMA emaTrend;
        private double dailyPnLBaseline;
        private double lastKnownStrategyPnL;
        private DateTime trackedPnLDate;
        private bool hasTrackedPnLDate;
        private bool dailyPnLLimitReached;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Variante del bot de velas consecutivas con filtro tendencial. Si ADX detecta tendencia, no abre operaciones.";
                Name = "ConsecutiveBarsReversalTrendFilterBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 1;
                DefaultQuantity = 1;

                ConsecutiveBars = 3;
                InvertLogic = false;

                UseTimeFilter = false;
                TradingStart = 9.50;
                TradingEnd = 17.00;
                CloseOutsideSchedule = false;

                UseTrendFilter = true;
                AdxPeriod = 14;
                AdxTrendThreshold = 25;
                UseEmaTrendFilter = false;
                EmaTrendPeriod = 200;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;
                BreakEvenTriggerTicks = 12;
                BreakEvenPlusTicks = 1;

                UseDailyProfitLimit = false;
                DailyProfitLimit = 300;
                UseDailyLossLimit = false;
                DailyLossLimit = 300;
            }
            else if (State == State.Configure)
            {
                ValidateQuarterHourInput(TradingStart, nameof(TradingStart));
                ValidateQuarterHourInput(TradingEnd, nameof(TradingEnd));

                startMinutes = ConvertQuarterHourToMinutes(TradingStart);
                endMinutes = ConvertQuarterHourToMinutes(TradingEnd);
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                emaTrend = EMA(EmaTrendPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            double strategyNetPnL = GetStrategyNetPnL();
            ResetDailyPnLTrackingIfNeeded(strategyNetPnL);

            try
            {
                bool insideTradingWindow = IsWithinTradingWindow();
                double dailyPnL = strategyNetPnL - dailyPnLBaseline;

                if (HasReachedDailyPnLLimit(dailyPnL))
                {
                    if (Position.MarketPosition != MarketPosition.Flat)
                        ExitOpenPosition("DailyPnLLimitExit");

                    return;
                }

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    UpdateBreakEven();

                    if (CloseOutsideSchedule && !insideTradingWindow)
                        ExitOpenPosition("OutsideScheduleExit");

                    return;
                }

                if (CurrentBar + 1 < ConsecutiveBars)
                    return;

                if (UseTrendFilter && CurrentBar < AdxPeriod)
                    return;

                if (UseEmaTrendFilter && CurrentBar < EmaTrendPeriod - 1)
                    return;

                if (!insideTradingWindow)
                    return;

                if (IsTrendingMarket())
                    return;

                if (!IsAboveEmaTrendFilter())
                    return;

                bool longSignal = InvertLogic ? HasBullishSequence() : HasBearishSequence();
                bool shortSignal = InvertLogic ? HasBearishSequence() : HasBullishSequence();

                if (longSignal)
                {
                    PrepareProtectiveOrders(LongSignalName);
                    breakEvenMoved = false;
                    EnterLong(LongSignalName);
                    return;
                }

                if (shortSignal)
                {
                    PrepareProtectiveOrders(ShortSignalName);
                    breakEvenMoved = false;
                    EnterShort(ShortSignalName);
                }
            }
            finally
            {
                lastKnownStrategyPnL = strategyNetPnL;
            }
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, ProfitTargetTicks);
        }

        private void UpdateBreakEven()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                breakEvenMoved = false;
                return;
            }

            if (breakEvenMoved || BreakEvenTriggerTicks <= 0)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double profitTicks = (Close[0] - Position.AveragePrice) / TickSize;
                if (profitTicks < BreakEvenTriggerTicks)
                    return;

                double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Position.AveragePrice + (BreakEvenPlusTicks * TickSize));

                SetStopLoss(LongSignalName, CalculationMode.Price, breakEvenPrice, false);
                breakEvenMoved = true;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                double profitTicks = (Position.AveragePrice - Close[0]) / TickSize;
                if (profitTicks < BreakEvenTriggerTicks)
                    return;

                double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Position.AveragePrice - (BreakEvenPlusTicks * TickSize));

                SetStopLoss(ShortSignalName, CalculationMode.Price, breakEvenPrice, false);
                breakEvenMoved = true;
            }
        }

        private bool IsTrendingMarket()
        {
            if (!UseTrendFilter)
                return false;

            return adx[0] >= AdxTrendThreshold;
        }

        private bool IsAboveEmaTrendFilter()
        {
            if (!UseEmaTrendFilter)
                return true;

            return Close[0] > emaTrend[0];
        }

        private double GetStrategyNetPnL()
        {
            double realizedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;

            if (Position.MarketPosition == MarketPosition.Flat)
                return realizedPnL;

            return realizedPnL + Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
        }

        private void ResetDailyPnLTrackingIfNeeded(double strategyNetPnL)
        {
            DateTime currentBarDate = Time[0].Date;

            if (!hasTrackedPnLDate)
            {
                trackedPnLDate = currentBarDate;
                dailyPnLBaseline = CurrentBar == 0 ? strategyNetPnL : lastKnownStrategyPnL;
                dailyPnLLimitReached = false;
                hasTrackedPnLDate = true;
                return;
            }

            if (currentBarDate == trackedPnLDate)
                return;

            trackedPnLDate = currentBarDate;
            dailyPnLBaseline = lastKnownStrategyPnL;
            dailyPnLLimitReached = false;
        }

        private bool HasReachedDailyPnLLimit(double dailyPnL)
        {
            if (dailyPnLLimitReached)
                return true;

            if (UseDailyProfitLimit && dailyPnL >= DailyProfitLimit)
            {
                dailyPnLLimitReached = true;
                return true;
            }

            if (UseDailyLossLimit && dailyPnL <= -DailyLossLimit)
            {
                dailyPnLLimitReached = true;
                return true;
            }

            return false;
        }

        private bool HasBearishSequence()
        {
            return HasExactSequence(IsBearishBar);
        }

        private bool HasBullishSequence()
        {
            return HasExactSequence(IsBullishBar);
        }

        private bool HasExactSequence(Func<int, bool> barValidator)
        {
            for (int barsAgo = 0; barsAgo < ConsecutiveBars; barsAgo++)
            {
                if (!barValidator(barsAgo))
                    return false;
            }

            if (CurrentBar >= ConsecutiveBars && barValidator(ConsecutiveBars))
                return false;

            return true;
        }

        private bool IsBullishBar(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsBearishBar(int barsAgo)
        {
            return Close[barsAgo] < Open[barsAgo];
        }

        private bool IsWithinTradingWindow()
        {
            if (!UseTimeFilter || startMinutes == endMinutes)
                return true;

            int currentMinutes = (Time[0].Hour * 60) + Time[0].Minute;

            if (startMinutes < endMinutes)
                return currentMinutes >= startMinutes && currentMinutes <= endMinutes;

            return currentMinutes >= startMinutes || currentMinutes <= endMinutes;
        }

        private void ExitOpenPosition(string exitSignalName)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(exitSignalName, LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(exitSignalName, ShortSignalName);
        }

        private void ValidateQuarterHourInput(double value, string parameterName)
        {
            if (value < 0 || value > 23.75)
                throw new ArgumentOutOfRangeException(parameterName, parameterName + " debe estar entre 0.00 y 23.75.");

            double quarterValue = value * 4.0;
            if (Math.Abs(quarterValue - Math.Round(quarterValue)) > 0.0001)
            {
                throw new ArgumentException(
                    parameterName + " solo acepta incrementos de 0.25. Ejemplos validos: 9.00, 9.25, 9.50, 9.75.",
                    parameterName);
            }
        }

        private int ConvertQuarterHourToMinutes(double value)
        {
            return (int)Math.Round(value * 4.0) * 15;
        }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Velas consecutivas", GroupName = "01. Senal", Order = 0)]
        public int ConsecutiveBars
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Invertir logica", GroupName = "01. Senal", Order = 1)]
        public bool InvertLogic
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "02. Horario", Order = 0)]
        public bool UseTimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora inicio", Description = "Formato en cuartos de hora. Ejemplo: 20.50 = 20:30.", GroupName = "02. Horario", Order = 1)]
        public double TradingStart
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora fin", Description = "Formato en cuartos de hora. Ejemplo: 21.75 = 21:45.", GroupName = "02. Horario", Order = 2)]
        public double TradingEnd
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cerrar fuera de horario", GroupName = "02. Horario", Order = 3)]
        public bool CloseOutsideSchedule
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro tendencial", GroupName = "03. Tendencia", Order = 0)]
        public bool UseTrendFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "ADX periodo", GroupName = "03. Tendencia", Order = 1)]
        public int AdxPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 100.0)]
        [Display(Name = "ADX umbral tendencia", GroupName = "03. Tendencia", Order = 2)]
        public double AdxTrendThreshold
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro EMA", GroupName = "03. Tendencia", Order = 3)]
        public bool UseEmaTrendFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA periodo", GroupName = "03. Tendencia", Order = 4)]
        public int EmaTrendPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "04. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "04. Riesgo", Order = 1)]
        public int ProfitTargetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trigger break even (ticks)", GroupName = "04. Riesgo", Order = 2)]
        public int BreakEvenTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset break even (ticks)", GroupName = "04. Riesgo", Order = 3)]
        public int BreakEvenPlusTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar profit diario", GroupName = "05. Limites diarios", Order = 0)]
        public bool UseDailyProfitLimit
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit diario ($)", GroupName = "05. Limites diarios", Order = 1)]
        public int DailyProfitLimit
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar stop diario", GroupName = "05. Limites diarios", Order = 2)]
        public bool UseDailyLossLimit
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop diario ($)", GroupName = "05. Limites diarios", Order = 3)]
        public int DailyLossLimit
        { get; set; }
    }
}
