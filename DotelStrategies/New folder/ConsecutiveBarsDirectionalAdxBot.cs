#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ConsecutiveBarsDirectionalAdxBot : Strategy
    {
        public enum TradeBias
        {
            Both,
            LongOnly,
            ShortOnly
        }

        private const string LongSignalName = "SeqDirLong";
        private const string ShortSignalName = "SeqDirShort";

        private ADX adx;
        private int startMinutes;
        private int endMinutes;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Compra tras X velas alcistas consecutivas y vende tras X velas bajistas consecutivas. Incluye filtro ADX minimo opcional, filtro horario y selector de direccion.";
                Name = "ConsecutiveBarsDirectionalAdxBot";
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
                TradeDirection = TradeBias.Both;

                AdxPeriod = 14;
                MinimumAdx = 0;

                UseTimeFilter = false;
                StartTime = 9.50;
                EndTime = 17.00;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;
            }
            else if (State == State.Configure)
            {
                ValidateQuarterHourInput(StartTime, nameof(StartTime));
                ValidateQuarterHourInput(EndTime, nameof(EndTime));

                startMinutes = ConvertQuarterHourToMinutes(StartTime);
                endMinutes = ConvertQuarterHourToMinutes(EndTime);
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            int requiredBars = Math.Max(ConsecutiveBars, MinimumAdx > 0 ? AdxPeriod : 1);
            if (CurrentBar + 1 < requiredBars)
                return;

            bool bullishSignal = HasBullishSequence();
            bool bearishSignal = HasBearishSequence();
            bool insideTradingWindow = IsWithinTradingWindow();
            bool adxFilterPassed = IsAdxFilterPassed();
            bool canLong = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.LongOnly;
            bool canShort = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.ShortOnly;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (!bearishSignal)
                    return;

                if (canShort && insideTradingWindow && adxFilterPassed)
                {
                    PrepareProtectiveOrders(ShortSignalName);
                    EnterShort(ShortSignalName);
                    return;
                }

                ExitLong("OppositeSignalExit", LongSignalName);
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (!bullishSignal)
                    return;

                if (canLong && insideTradingWindow && adxFilterPassed)
                {
                    PrepareProtectiveOrders(LongSignalName);
                    EnterLong(LongSignalName);
                    return;
                }

                ExitShort("OppositeSignalExit", ShortSignalName);
                return;
            }

            if (!insideTradingWindow || !adxFilterPassed)
                return;

            if (canLong && bullishSignal)
            {
                PrepareProtectiveOrders(LongSignalName);
                EnterLong(LongSignalName);
                return;
            }

            if (canShort && bearishSignal)
            {
                PrepareProtectiveOrders(ShortSignalName);
                EnterShort(ShortSignalName);
            }
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, ProfitTargetTicks);
        }

        private bool IsAdxFilterPassed()
        {
            if (MinimumAdx <= 0)
                return true;

            return adx[0] >= MinimumAdx;
        }

        private bool HasBullishSequence()
        {
            return HasExactSequence(IsBullishBar);
        }

        private bool HasBearishSequence()
        {
            return HasExactSequence(IsBearishBar);
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
        [Display(Name = "Direccion", GroupName = "01. Senal", Order = 1)]
        public TradeBias TradeDirection
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX periodo", GroupName = "02. Filtro ADX", Order = 0)]
        public int AdxPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX minimo", Description = "0 desactiva el filtro. Si es mayor que 0, solo se permite operar cuando ADX es igual o superior a este nivel.", GroupName = "02. Filtro ADX", Order = 1)]
        public double MinimumAdx
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "03. Horario", Order = 0)]
        public bool UseTimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora inicio", Description = "Formato en cuartos de hora. Ejemplo: 9.50 = 9:30.", GroupName = "03. Horario", Order = 1)]
        public double StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora fin", Description = "Formato en cuartos de hora. Ejemplo: 17.00 = 17:00.", GroupName = "03. Horario", Order = 2)]
        public double EndTime
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
    }
}
