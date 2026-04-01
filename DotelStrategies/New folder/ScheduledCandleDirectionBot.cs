#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ScheduledCandleDirectionBot : Strategy
    {
        private const string LongSignalName = "ScheduledLong";
        private const string ShortSignalName = "ScheduledShort";
        private const int DisabledTimeMinutes = -1;

        private SMA sma;
        private RSI rsi;
        private DateTime currentTradingDate;
        private bool tradingDateInitialized;
        private bool[] processedSlotsToday;
        private int[] scheduledMinutes;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Evalua la primera vela cerrada a partir de hasta 4 horas configuradas y entra segun la direccion de la vela trigger. Incluye inversion de logica, filtros opcionales de SMA, RSI y volumen, y TP/SL en ticks.";
                Name = "ScheduledCandleDirectionBot";
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

                InvertLogic = false;

                TradeTime1 = 9.00;
                TradeTime2 = -1;
                TradeTime3 = -1;
                TradeTime4 = -1;

                UseMovingAverageFilter = false;
                MovingAveragePeriod = 20;

                UseRsiFilter = false;
                RsiPeriod = 14;
                RsiSmoothing = 3;
                RsiLevel = 50;

                UseVolumeFilter = false;
                MinTriggerVolume = 1000;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;
            }
            else if (State == State.Configure)
            {
                scheduledMinutes = new[]
                {
                    ConvertQuarterHourOrDisabledToMinutes(TradeTime1, nameof(TradeTime1)),
                    ConvertQuarterHourOrDisabledToMinutes(TradeTime2, nameof(TradeTime2)),
                    ConvertQuarterHourOrDisabledToMinutes(TradeTime3, nameof(TradeTime3)),
                    ConvertQuarterHourOrDisabledToMinutes(TradeTime4, nameof(TradeTime4))
                };

                ValidateUniqueActiveTradeTimes();
                processedSlotsToday = new bool[scheduledMinutes.Length];
            }
            else if (State == State.DataLoaded)
            {
                sma = SMA(MovingAveragePeriod);
                rsi = RSI(RsiPeriod, RsiSmoothing);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < GetRequiredBarsCount() - 1)
                return;

            ResetDailyStateIfNeeded();

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            int triggeredSlotIndex = GetTriggeredTradeSlotIndex();
            if (triggeredSlotIndex < 0)
                return;

            processedSlotsToday[triggeredSlotIndex] = true;

            if (IsDojiBar(0))
                return;

            bool triggerBullish = IsBullishBar(0);
            bool enterLong = InvertLogic ? !triggerBullish : triggerBullish;

            if (!PassesOptionalFilters(enterLong))
                return;

            string signalName = enterLong ? LongSignalName : ShortSignalName;
            PrepareProtectiveOrders(signalName);

            if (enterLong)
                EnterLong(signalName);
            else
                EnterShort(signalName);
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, ProfitTargetTicks);
        }

        private bool PassesOptionalFilters(bool isLong)
        {
            if (UseMovingAverageFilter)
            {
                if (isLong && Close[0] <= sma[0])
                    return false;

                if (!isLong && Close[0] >= sma[0])
                    return false;
            }

            if (UseRsiFilter)
            {
                if (isLong && rsi[0] <= RsiLevel)
                    return false;

                if (!isLong && rsi[0] >= RsiLevel)
                    return false;
            }

            if (UseVolumeFilter && Volume[0] <= MinTriggerVolume)
                return false;

            return true;
        }

        private int GetTriggeredTradeSlotIndex()
        {
            int currentMinutes = ConvertTimeToMinutes(Time[0]);
            int previousMinutes = CurrentBar == 0 || Time[1].Date != Time[0].Date
                ? DisabledTimeMinutes
                : ConvertTimeToMinutes(Time[1]);

            for (int slotIndex = 0; slotIndex < scheduledMinutes.Length; slotIndex++)
            {
                int slotMinutes = scheduledMinutes[slotIndex];

                if (slotMinutes == DisabledTimeMinutes || processedSlotsToday[slotIndex])
                    continue;

                if (currentMinutes < slotMinutes)
                    continue;

                if (previousMinutes >= slotMinutes)
                    continue;

                return slotIndex;
            }

            return -1;
        }

        private int GetRequiredBarsCount()
        {
            int requiredBars = 1;

            if (UseMovingAverageFilter)
                requiredBars = Math.Max(requiredBars, MovingAveragePeriod);

            if (UseRsiFilter)
                requiredBars = Math.Max(requiredBars, RsiPeriod + RsiSmoothing);

            return requiredBars;
        }

        private void ResetDailyStateIfNeeded()
        {
            DateTime barDate = Time[0].Date;

            if (!tradingDateInitialized || currentTradingDate != barDate)
            {
                currentTradingDate = barDate;
                tradingDateInitialized = true;
                Array.Clear(processedSlotsToday, 0, processedSlotsToday.Length);
            }
        }

        private bool IsBullishBar(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsDojiBar(int barsAgo)
        {
            return Close[barsAgo] == Open[barsAgo];
        }

        private int ConvertQuarterHourOrDisabledToMinutes(double value, string parameterName)
        {
            if (Math.Abs(value - (-1.0)) <= 0.0001)
                return DisabledTimeMinutes;

            ValidateQuarterHourInput(value, parameterName);
            return (int)Math.Round(value * 4.0) * 15;
        }

        private void ValidateQuarterHourInput(double value, string parameterName)
        {
            if (value < 0 || value > 23.75)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    parameterName + " debe estar entre 0.00 y 23.75, o usar -1 para desactivarlo.");
            }

            double quarterValue = value * 4.0;
            if (Math.Abs(quarterValue - Math.Round(quarterValue)) > 0.0001)
            {
                throw new ArgumentException(
                    parameterName + " solo acepta incrementos de 0.25. Ejemplos validos: 9.00, 9.25, 9.50, 9.75.",
                    parameterName);
            }
        }

        private void ValidateUniqueActiveTradeTimes()
        {
            HashSet<int> activeTimes = new HashSet<int>();

            foreach (int slotMinutes in scheduledMinutes)
            {
                if (slotMinutes == DisabledTimeMinutes)
                    continue;

                if (!activeTimes.Add(slotMinutes))
                    throw new ArgumentException("Las horas activas no pueden repetirse.");
            }
        }

        private int ConvertTimeToMinutes(DateTime time)
        {
            return (time.Hour * 60) + time.Minute;
        }

        [NinjaScriptProperty]
        [Display(Name = "Invertir logica", GroupName = "01. Senal", Order = 0)]
        public bool InvertLogic
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora 1", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar.", GroupName = "02. Horarios", Order = 0)]
        public double TradeTime1
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora 2", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar.", GroupName = "02. Horarios", Order = 1)]
        public double TradeTime2
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora 3", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar.", GroupName = "02. Horarios", Order = 2)]
        public double TradeTime3
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora 4", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar.", GroupName = "02. Horarios", Order = 3)]
        public double TradeTime4
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro SMA", GroupName = "03. Filtro MA", Order = 0)]
        public bool UseMovingAverageFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Periodo SMA", GroupName = "03. Filtro MA", Order = 1)]
        public int MovingAveragePeriod
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro RSI", GroupName = "04. Filtro RSI", Order = 0)]
        public bool UseRsiFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "RSI periodo", GroupName = "04. Filtro RSI", Order = 1)]
        public int RsiPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "RSI smoothing", GroupName = "04. Filtro RSI", Order = 2)]
        public int RsiSmoothing
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Nivel RSI", GroupName = "04. Filtro RSI", Order = 3)]
        public double RsiLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro volumen", GroupName = "05. Filtro Volumen", Order = 0)]
        public bool UseVolumeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(typeof(long), "0", "9223372036854775807")]
        [Display(Name = "Volumen minimo trigger", Description = "Solo entra si el volumen de la vela trigger es mayor que este valor.", GroupName = "05. Filtro Volumen", Order = 1)]
        public long MinTriggerVolume
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "06. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "06. Riesgo", Order = 1)]
        public int ProfitTargetTicks
        { get; set; }
    }
}
