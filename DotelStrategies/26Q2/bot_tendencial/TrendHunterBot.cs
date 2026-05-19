#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TrendHunterBot : Strategy
    {
        private const string LongSignalName = "TrendHunterLong";
        private const string ShortSignalName = "TrendHunterShort";

        private EMA emaFast;
        private EMA emaSlow;
        private int startMinutes;
        private int endMinutes;
        private int requiredBars;
        private bool chartVisualsEnabled;
        private bool configurationValid;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Bot tendencial simple: opera cruces de EMA filtrados por pendiente de EMA lenta, con filtro horario y SL/TP visual.";
                Name = "TrendHunterBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 50;
                DefaultQuantity = 1;
                TraceOrders = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;

                FastEmaPeriod = 12;
                SlowEmaPeriod = 48;
                UseSlopeFilter = true;
                SlopeLookbackBars = 10;
                MinSlowEmaSlopeTicks = 4.0;

                UseTimeFilter = true;
                StartTime = 9.50;
                EndTime = 16.00;

                OrderQuantity = 1;
                StopLossTicks = 24;
                RewardRiskMultiple = 2.0;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();
                configurationValid = FastEmaPeriod < SlowEmaPeriod;
                startMinutes = ConvertQuarterHourToMinutes(StartTime);
                endMinutes = ConvertQuarterHourToMinutes(EndTime);
                requiredBars = GetRequiredBarsCount();
                BarsRequiredToTrade = requiredBars;
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(FastEmaPeriod);
                emaSlow = EMA(SlowEmaPeriod);
                chartVisualsEnabled = ChartControl != null;

                if (chartVisualsEnabled)
                {
                    emaFast.Plots[0].Brush = Brushes.DodgerBlue;
                    emaSlow.Plots[0].Brush = Brushes.Goldenrod;
                    AddChartIndicator(emaFast);
                    AddChartIndicator(emaSlow);
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (!configurationValid)
                return;

            if (CurrentBar + 1 < requiredBars)
                return;

            bool crossUp = emaFast[0] > emaSlow[0] && emaFast[1] <= emaSlow[1];
            bool crossDown = emaFast[0] < emaSlow[0] && emaFast[1] >= emaSlow[1];
            double slowSlopeTicks = UseSlopeFilter ? GetSlowEmaSlopeTicks() : 0.0;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (crossDown)
                {
                    DrawExitVisual(MarketPosition.Long);
                    ExitLong("OppositeCrossExit", LongSignalName);
                }

                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (crossUp)
                {
                    DrawExitVisual(MarketPosition.Short);
                    ExitShort("OppositeCrossExit", ShortSignalName);
                }

                return;
            }

            if (UseTimeFilter && !IsWithinTradingWindow())
                return;

            if (crossUp && IsLongSlopeAllowed(slowSlopeTicks))
            {
                int targetTicks;
                PrepareProtectiveOrders(LongSignalName, out targetTicks);
                DrawEntryVisual(MarketPosition.Long, Close[0], StopLossTicks, targetTicks, slowSlopeTicks);
                EnterLong(OrderQuantity, LongSignalName);
                return;
            }

            if (crossDown && IsShortSlopeAllowed(slowSlopeTicks))
            {
                int targetTicks;
                PrepareProtectiveOrders(ShortSignalName, out targetTicks);
                DrawEntryVisual(MarketPosition.Short, Close[0], StopLossTicks, targetTicks, slowSlopeTicks);
                EnterShort(OrderQuantity, ShortSignalName);
            }
        }

        private void PrepareProtectiveOrders(string signalName, out int targetTicks)
        {
            targetTicks = Math.Max(1, (int)Math.Round(StopLossTicks * RewardRiskMultiple));

            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, targetTicks);
        }

        private double GetSlowEmaSlopeTicks()
        {
            return (emaSlow[0] - emaSlow[SlopeLookbackBars]) / TickSize;
        }

        private bool IsLongSlopeAllowed(double slowSlopeTicks)
        {
            return !UseSlopeFilter || slowSlopeTicks >= MinSlowEmaSlopeTicks;
        }

        private bool IsShortSlopeAllowed(double slowSlopeTicks)
        {
            return !UseSlopeFilter || slowSlopeTicks <= -MinSlowEmaSlopeTicks;
        }

        private void DrawEntryVisual(MarketPosition direction, double entryPrice, int stopTicks, int targetTicks, double slopeTicks)
        {
            if (!chartVisualsEnabled)
                return;

            string baseTag = Name + "_" + CurrentBar + "_" + (direction == MarketPosition.Long ? "L" : "S");
            double stopPrice = direction == MarketPosition.Long
                ? entryPrice - (stopTicks * TickSize)
                : entryPrice + (stopTicks * TickSize);
            double targetPrice = direction == MarketPosition.Long
                ? entryPrice + (targetTicks * TickSize)
                : entryPrice - (targetTicks * TickSize);

            double markerOffset = Math.Max(TickSize * 4, (High[0] - Low[0]) * 0.35);

            if (direction == MarketPosition.Long)
                Draw.ArrowUp(this, baseTag + "_ENTRY_ARROW", false, 0, Low[0] - markerOffset, Brushes.LimeGreen);
            else
                Draw.ArrowDown(this, baseTag + "_ENTRY_ARROW", false, 0, High[0] + markerOffset, Brushes.OrangeRed);

            Draw.Text(
                this,
                baseTag + "_ENTRY_TEXT",
                (direction == MarketPosition.Long ? "LONG" : "SHORT") + " slope " + slopeTicks.ToString("F1") + "t",
                0,
                direction == MarketPosition.Long ? Low[0] - (markerOffset * 1.8) : High[0] + (markerOffset * 1.8),
                direction == MarketPosition.Long ? Brushes.LimeGreen : Brushes.OrangeRed);

            Draw.Ray(this, baseTag + "_ENTRY", 0, entryPrice, 1, entryPrice, Brushes.Gold);
            Draw.Ray(this, baseTag + "_SL", 0, stopPrice, 1, stopPrice, Brushes.OrangeRed);
            Draw.Ray(this, baseTag + "_TP", 0, targetPrice, 1, targetPrice, Brushes.LimeGreen);
            Draw.Text(this, baseTag + "_SL_TEXT", "SL", 0, stopPrice, Brushes.OrangeRed);
            Draw.Text(this, baseTag + "_TP_TEXT", "TP", 0, targetPrice, Brushes.LimeGreen);
        }

        private void DrawExitVisual(MarketPosition positionBeingClosed)
        {
            if (!chartVisualsEnabled)
                return;

            string baseTag = Name + "_" + CurrentBar + "_EXIT";
            double markerOffset = Math.Max(TickSize * 4, (High[0] - Low[0]) * 0.35);
            double y = positionBeingClosed == MarketPosition.Long
                ? High[0] + markerOffset
                : Low[0] - markerOffset;

            Draw.Diamond(this, baseTag + "_MARK", false, 0, y, Brushes.White);
            Draw.Text(this, baseTag + "_TEXT", "EXIT", 0, y, Brushes.White);
        }

        private bool IsWithinTradingWindow()
        {
            if (startMinutes == endMinutes)
                return true;

            int currentMinutes = (Time[0].Hour * 60) + Time[0].Minute;

            if (startMinutes < endMinutes)
                return currentMinutes >= startMinutes && currentMinutes <= endMinutes;

            return currentMinutes >= startMinutes || currentMinutes <= endMinutes;
        }

        private int GetRequiredBarsCount()
        {
            int required = Math.Max(FastEmaPeriod, SlowEmaPeriod);
            required = Math.Max(required, SlowEmaPeriod + SlopeLookbackBars + 2);
            return Math.Max(required, 5);
        }

        private void ValidateConfiguration()
        {
            ValidateQuarterHourInput(StartTime, nameof(StartTime));
            ValidateQuarterHourInput(EndTime, nameof(EndTime));

            if (StopLossTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(StopLossTicks), "StopLossTicks debe ser mayor que 0.");

            if (RewardRiskMultiple <= 0)
                throw new ArgumentOutOfRangeException(nameof(RewardRiskMultiple), "RewardRiskMultiple debe ser mayor que 0.");
        }

        private void ValidateQuarterHourInput(double value, string parameterName)
        {
            if (value < 0 || value > 23.75)
                throw new ArgumentOutOfRangeException(parameterName, parameterName + " debe estar entre 0.00 y 23.75.");

            double quarterValue = value * 4.0;
            if (Math.Abs(quarterValue - Math.Round(quarterValue)) > 0.0001)
                throw new ArgumentException(parameterName + " solo acepta incrementos de 0.25.", parameterName);
        }

        private int ConvertQuarterHourToMinutes(double value)
        {
            return (int)Math.Round(value * 4.0) * 15;
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "EMA rapida", GroupName = "01. Senal", Order = 0)]
        public int FastEmaPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "EMA lenta", GroupName = "01. Senal", Order = 1)]
        public int SlowEmaPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro pendiente", GroupName = "01. Senal", Order = 2)]
        public bool UseSlopeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Velas pendiente", GroupName = "01. Senal", Order = 3)]
        public int SlopeLookbackBars
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Pendiente minima ticks", Description = "Pendiente minima de la EMA lenta durante el lookback. Positiva para largos, negativa para cortos.", GroupName = "01. Senal", Order = 4)]
        public double MinSlowEmaSlopeTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "02. Horario", Order = 0)]
        public bool UseTimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora inicio", Description = "Formato en cuartos de hora. Ejemplo: 9.50 = 9:30.", GroupName = "02. Horario", Order = 1)]
        public double StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora fin", Description = "Formato en cuartos de hora. Ejemplo: 16.00 = 16:00.", GroupName = "02. Horario", Order = 2)]
        public double EndTime
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
        [Range(0.1, 20.0)]
        [Display(Name = "Multiplo TP/SL", Description = "TP en ticks = StopLossTicks * Multiplo TP/SL.", GroupName = "03. Riesgo", Order = 2)]
        public double RewardRiskMultiple
        { get; set; }
    }
}
