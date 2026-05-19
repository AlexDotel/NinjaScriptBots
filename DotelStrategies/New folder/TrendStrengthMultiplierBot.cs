#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TrendStrengthMultiplierBot : Strategy
    {
        public enum StrengthMetricMode
        {
            Adx,
            Atr
        }

        private const string LongSignalName = "StrengthLong";
        private const string ShortSignalName = "StrengthShort";

        private ADX adx;
        private ATR signalAtr;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Entra segun la direccion de la vela cuando la fuerza actual (ADX o ATR) supera por un multiplo a la fuerza de la vela anterior. Incluye filtro horario, selector de lado y riesgo por ticks.";
                Name = "TrendStrengthMultiplierBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                DefaultQuantity = 1;
                TraceOrders = false;

                StrengthMetric = StrengthMetricMode.Adx;
                StrengthMultiplier = 1.50;
                AdxPeriod = 14;
                SignalAtrPeriod = 14;

                EnableLongs = true;
                EnableShorts = true;

                UseTimeFilter = false;
                StartTime = 93000;
                EndTime = 160000;

                OrderQuantity = 1;
                StopLossTicks = 20;
                ProfitTargetTicks = 40;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();
                BarsRequiredToTrade = GetRequiredBarsCount();
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                signalAtr = ATR(SignalAtrPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar + 1 < GetRequiredBarsCount())
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (!IsWithinTradingWindow(ToTime(Time[0])))
                return;

            if (!HasStrengthExpansion())
                return;

            if (IsBullishBar(0) && EnableLongs)
            {
                SubmitEntry(MarketPosition.Long);
                return;
            }

            if (IsBearishBar(0) && EnableShorts)
                SubmitEntry(MarketPosition.Short);
        }

        private void SubmitEntry(MarketPosition direction)
        {
            string signalName = direction == MarketPosition.Long ? LongSignalName : ShortSignalName;
            PrepareProtectiveOrders(signalName);

            if (direction == MarketPosition.Long)
                EnterLong(OrderQuantity, signalName);
            else
                EnterShort(OrderQuantity, signalName);
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, ProfitTargetTicks);
        }

        private bool HasStrengthExpansion()
        {
            double currentStrength = GetStrengthValue(0);
            double previousStrength = GetStrengthValue(1);

            if (!IsValidStrengthValue(currentStrength) || !IsValidStrengthValue(previousStrength))
                return false;

            if (previousStrength <= 0)
                return false;

            return currentStrength >= previousStrength * StrengthMultiplier;
        }

        private double GetStrengthValue(int barsAgo)
        {
            return StrengthMetric == StrengthMetricMode.Adx
                ? adx[barsAgo]
                : signalAtr[barsAgo];
        }

        private bool IsValidStrengthValue(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value > 0;
        }

        private bool IsBullishBar(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsBearishBar(int barsAgo)
        {
            return Close[barsAgo] < Open[barsAgo];
        }

        private bool IsWithinTradingWindow(int currentTime)
        {
            if (!UseTimeFilter || StartTime == EndTime)
                return true;

            if (StartTime < EndTime)
                return currentTime >= StartTime && currentTime <= EndTime;

            return currentTime >= StartTime || currentTime <= EndTime;
        }

        private int GetRequiredBarsCount()
        {
            int requiredBars = 2;

            if (StrengthMetric == StrengthMetricMode.Adx)
                requiredBars = Math.Max(requiredBars, AdxPeriod + 1);
            else
                requiredBars = Math.Max(requiredBars, SignalAtrPeriod + 1);

            return requiredBars;
        }

        private void ValidateConfiguration()
        {
            if (!EnableLongs && !EnableShorts)
                throw new ArgumentException("Debes habilitar compras, ventas o ambas.");

            if (StrengthMultiplier <= 0)
                throw new ArgumentOutOfRangeException("StrengthMultiplier", "StrengthMultiplier debe ser mayor que 0.");

            if (AdxPeriod <= 0)
                throw new ArgumentOutOfRangeException("AdxPeriod", "AdxPeriod debe ser mayor que 0.");

            if (SignalAtrPeriod <= 0)
                throw new ArgumentOutOfRangeException("SignalAtrPeriod", "SignalAtrPeriod debe ser mayor que 0.");

            if (UseTimeFilter)
            {
                ValidateTimeValue(StartTime, "StartTime");
                ValidateTimeValue(EndTime, "EndTime");
            }

            if (OrderQuantity <= 0)
                throw new ArgumentOutOfRangeException("OrderQuantity", "OrderQuantity debe ser mayor o igual que 1.");

            if (StopLossTicks <= 0)
                throw new ArgumentOutOfRangeException("StopLossTicks", "StopLossTicks debe ser mayor que 0.");

            if (ProfitTargetTicks <= 0)
                throw new ArgumentOutOfRangeException("ProfitTargetTicks", "ProfitTargetTicks debe ser mayor que 0.");
        }

        private void ValidateTimeValue(int value, string parameterName)
        {
            if (value < 0 || value > 235959)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    parameterName + " debe estar en formato HHmmss entre 000000 y 235959.");
            }

            int hours = value / 10000;
            int minutes = (value / 100) % 100;
            int seconds = value % 100;

            if (hours > 23 || minutes > 59 || seconds > 59)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    parameterName + " no es una hora valida en formato HHmmss.");
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Fuerza", GroupName = "01. Senal", Order = 0)]
        public StrengthMetricMode StrengthMetric
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "Multiplo fuerza", Description = "La fuerza actual debe ser al menos este multiplo de la fuerza de la vela anterior.", GroupName = "01. Senal", Order = 1)]
        public double StrengthMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX periodo", GroupName = "01. Senal", Order = 2)]
        public int AdxPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR periodo senal", GroupName = "01. Senal", Order = 3)]
        public int SignalAtrPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar compras", GroupName = "01. Senal", Order = 4)]
        public bool EnableLongs
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar ventas", GroupName = "01. Senal", Order = 5)]
        public bool EnableShorts
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "02. Horario", Order = 0)]
        public bool UseTimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora inicio (HHmmss)", GroupName = "02. Horario", Order = 1)]
        public int StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora fin (HHmmss)", GroupName = "02. Horario", Order = 2)]
        public int EndTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Cantidad", GroupName = "03. Riesgo", Order = 0)]
        public int OrderQuantity
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Stop loss ticks", GroupName = "03. Riesgo", Order = 1)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Take profit ticks", GroupName = "03. Riesgo", Order = 2)]
        public int ProfitTargetTicks
        { get; set; }
    }
}
