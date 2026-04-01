#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ConsecutiveBarsReversalBot : Strategy
    {
        private const string LongSignalName = "SeqLong";
        private const string ShortSignalName = "SeqShort";

        private bool breakEvenMoved;
        private int startMinutes;
        private int endMinutes;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Abre largos tras X velas bajistas consecutivas y cortos tras X velas alcistas consecutivas. Incluye inversion de logica, filtro horario y gestion de riesgo.";
                Name = "ConsecutiveBarsReversalBot";
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

                StopLossTicks = 20;
                ProfitTargetTicks = 40;
                BreakEvenTriggerTicks = 12;
                BreakEvenPlusTicks = 1;
            }
            else if (State == State.Configure)
            {
                ValidateQuarterHourInput(TradingStart, nameof(TradingStart));
                ValidateQuarterHourInput(TradingEnd, nameof(TradingEnd));

                startMinutes = ConvertQuarterHourToMinutes(TradingStart);
                endMinutes = ConvertQuarterHourToMinutes(TradingEnd);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar + 1 < ConsecutiveBars)
                return;

            bool insideTradingWindow = IsWithinTradingWindow();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                UpdateBreakEven();

                if (CloseOutsideSchedule && !insideTradingWindow)
                    ExitOpenPosition();

                return;
            }

            if (!insideTradingWindow)
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

        private void ExitOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("OutsideScheduleExit", LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("OutsideScheduleExit", ShortSignalName);
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
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "03. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "03. Riesgo", Order = 1)]
        public int ProfitTargetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trigger break even (ticks)", GroupName = "03. Riesgo", Order = 2)]
        public int BreakEvenTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset break even (ticks)", GroupName = "03. Riesgo", Order = 3)]
        public int BreakEvenPlusTicks
        { get; set; }
    }
}
