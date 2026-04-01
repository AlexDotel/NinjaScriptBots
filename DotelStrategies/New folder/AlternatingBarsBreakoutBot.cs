#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AlternatingBarsBreakoutBot : Strategy
    {
        public enum TradeBias
        {
            Both,
            LongOnly,
            ShortOnly
        }

        private const string LongSignalName = "AltLong";
        private const string ShortSignalName = "AltShort";

        private bool breakEvenMoved;
        private readonly HashSet<string> countedEntryOrderIds = new HashSet<string>();
        private DateTime currentTradingDate;
        private bool tradingDateInitialized;
        private int tradesToday;
        private int startMinutes;
        private int endMinutes;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Busca bloques alternados configurables y entra tras un breakout configurable de velas consecutivas. Incluye TP, SL, break even, filtro horario y limite diario.";
                Name = "AlternatingBarsBreakoutBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 3;
                DefaultQuantity = 1;

                AlternationBlocksCount = 10;
                AlternationBlockSize = 1;
                BreakoutBarsCount = 2;
                TradeDirection = TradeBias.Both;

                UseTimeFilter = false;
                StartTime = 9.50;
                EndTime = 17.00;
                CloseOutsideSchedule = false;

                MaxTradesPerDay = 0;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;

                UseBreakEven = true;
                BreakEvenTriggerTicks = 12;
                BreakEvenPlusTicks = 1;
            }
            else if (State == State.Configure)
            {
                ValidateQuarterHourInput(StartTime, nameof(StartTime));
                ValidateQuarterHourInput(EndTime, nameof(EndTime));

                startMinutes = ConvertQuarterHourToMinutes(StartTime);
                endMinutes = ConvertQuarterHourToMinutes(EndTime);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < GetRequiredBarsCount() - 1)
                return;

            ResetDailyCountersIfNeeded();

            bool insideTradingWindow = IsWithinTradingWindow();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                UpdateBreakEven();

                if (CloseOutsideSchedule && !insideTradingWindow)
                    ExitOpenPosition("OutsideScheduleExit");

                return;
            }

            if (!insideTradingWindow)
                return;

            if (MaxTradesPerDay > 0 && tradesToday >= MaxTradesPerDay)
                return;

            bool canLong = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.LongOnly;
            bool canShort = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.ShortOnly;

            if (canLong && HasEntrySetup(true))
            {
                PrepareProtectiveOrders(LongSignalName);
                breakEvenMoved = false;
                EnterLong(LongSignalName);
                return;
            }

            if (canShort && HasEntrySetup(false))
            {
                PrepareProtectiveOrders(ShortSignalName);
                breakEvenMoved = false;
                EnterShort(ShortSignalName);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            if (execution.Order.Name != LongSignalName && execution.Order.Name != ShortSignalName)
                return;

            if (string.IsNullOrEmpty(execution.Order.OrderId))
                return;

            if (countedEntryOrderIds.Contains(execution.Order.OrderId))
                return;

            countedEntryOrderIds.Add(execution.Order.OrderId);
            tradesToday++;
        }

        private bool HasEntrySetup(bool bullishBreakout)
        {
            return HasSameDirectionBars(bullishBreakout, 0, BreakoutBarsCount)
                && HasAlternatingSequence(BreakoutBarsCount, bullishBreakout);
        }

        private bool HasSameDirectionBars(bool bullishBars, int startBarsAgo, int count)
        {
            for (int barsAgo = startBarsAgo; barsAgo < startBarsAgo + count; barsAgo++)
            {
                if (bullishBars && !IsBullishBar(barsAgo))
                    return false;

                if (!bullishBars && !IsBearishBar(barsAgo))
                    return false;
            }

            return true;
        }

        private bool HasAlternatingSequence(int startBarsAgo, bool bullishBreakout)
        {
            // El bloque mas reciente de la alternacion debe ser opuesto al breakout.
            for (int blockIndex = 0; blockIndex < AlternationBlocksCount; blockIndex++)
            {
                bool bullishBlock = (blockIndex % 2 == 0) ? !bullishBreakout : bullishBreakout;
                int blockStartBarsAgo = startBarsAgo + (blockIndex * AlternationBlockSize);

                if (!HasSameDirectionBars(bullishBlock, blockStartBarsAgo, AlternationBlockSize))
                    return false;
            }

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

            if (!UseBreakEven || breakEvenMoved || BreakEvenTriggerTicks <= 0)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double triggerPrice = Position.AveragePrice + (BreakEvenTriggerTicks * TickSize);
                if (High[0] < triggerPrice)
                    return;

                double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Position.AveragePrice + (BreakEvenPlusTicks * TickSize));

                SetStopLoss(LongSignalName, CalculationMode.Price, breakEvenPrice, false);
                breakEvenMoved = true;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                double triggerPrice = Position.AveragePrice - (BreakEvenTriggerTicks * TickSize);
                if (Low[0] > triggerPrice)
                    return;

                double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Position.AveragePrice - (BreakEvenPlusTicks * TickSize));

                SetStopLoss(ShortSignalName, CalculationMode.Price, breakEvenPrice, false);
                breakEvenMoved = true;
            }
        }

        private void ResetDailyCountersIfNeeded()
        {
            DateTime barDate = Time[0].Date;

            if (!tradingDateInitialized || currentTradingDate != barDate)
            {
                currentTradingDate = barDate;
                countedEntryOrderIds.Clear();
                tradesToday = 0;
                tradingDateInitialized = true;
            }
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

        private void ExitOpenPosition(string exitTag)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(exitTag, LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(exitTag, ShortSignalName);
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

        private int GetRequiredBarsCount()
        {
            return BreakoutBarsCount + (AlternationBlocksCount * AlternationBlockSize);
        }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "Bloques alternados", Description = "Cantidad de bloques que debe tener la alternacion antes del breakout.", GroupName = "01. Senal", Order = 0)]
        public int AlternationBlocksCount
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Velas por bloque", Description = "Ejemplo: 2 = dos alcistas, dos bajistas, dos alcistas...", GroupName = "01. Senal", Order = 1)]
        public int AlternationBlockSize
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Velas breakout", Description = "Cantidad de velas consecutivas en la misma direccion para entrar.", GroupName = "01. Senal", Order = 2)]
        public int BreakoutBarsCount
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Direccion", GroupName = "01. Senal", Order = 3)]
        public TradeBias TradeDirection
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "02. Horario", Order = 0)]
        public bool UseTimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora inicio", Description = "Formato en cuartos de hora. Ejemplo: 15.50 = 15:30.", GroupName = "02. Horario", Order = 1)]
        public double StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora fin", Description = "Formato en cuartos de hora. Ejemplo: 17.25 = 17:15.", GroupName = "02. Horario", Order = 2)]
        public double EndTime
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cerrar fuera de horario", GroupName = "02. Horario", Order = 3)]
        public bool CloseOutsideSchedule
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max operaciones por dia", Description = "0 = sin limite.", GroupName = "03. Riesgo", Order = 0)]
        public int MaxTradesPerDay
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "03. Riesgo", Order = 1)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "03. Riesgo", Order = 2)]
        public int ProfitTargetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar break even", GroupName = "03. Riesgo", Order = 3)]
        public bool UseBreakEven
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trigger break even (ticks)", GroupName = "03. Riesgo", Order = 4)]
        public int BreakEvenTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset break even (ticks)", GroupName = "03. Riesgo", Order = 5)]
        public int BreakEvenPlusTicks
        { get; set; }
    }
}
