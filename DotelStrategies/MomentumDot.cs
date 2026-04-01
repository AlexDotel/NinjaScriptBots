#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MomentumTimeFilterFast : Strategy
    {
        private int lastEntryBar = -999999;

        #region Inputs
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "MomentumPeriod", Order = 1, GroupName = "Parameters")]
        public int MomentumPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ThresholdTicks", Order = 2, GroupName = "Parameters")]
        public int ThresholdTicks { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "StopLossTicks", Order = 3, GroupName = "Parameters")]
        public int StopLossTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "TakeProfitTicks", Order = 4, GroupName = "Parameters")]
        public int TakeProfitTicks { get; set; } = 30;

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "CooldownBars", Order = 5, GroupName = "Parameters")]
        public int CooldownBars { get; set; } = 5;

        // Horario en formato HHmm (rápido para optimización)
        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "StartTimeHHmm", Order = 6, GroupName = "Time Filter")]
        public int StartTimeHHmm { get; set; } = 900;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "EndTimeHHmm", Order = 7, GroupName = "Time Filter")]
        public int EndTimeHHmm { get; set; } = 1700;

        [NinjaScriptProperty]
        [Display(Name = "ShowStatusOnChart", Order = 8, GroupName = "Visual")]
        public bool ShowStatusOnChart { get; set; } = true;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MomentumTimeFilterFast";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = true;

                // Para backtests más limpios/rápidos
                BarsRequiredToTrade = 20;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < MomentumPeriod)
                return;

            bool inWindow = IsInTradeWindow(Time[0]);

            if (ShowStatusOnChart)
                DrawStatus(inWindow);

            if (!inWindow)
                return;

            if (CooldownBars > 0 && CurrentBar - lastEntryBar < CooldownBars)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double momentumTicks = (Close[0] - Close[MomentumPeriod]) / TickSize;

            if (momentumTicks >= ThresholdTicks)
            {
                EnterLong("MOMO_L");
                lastEntryBar = CurrentBar;
            }
            else if (momentumTicks <= -ThresholdTicks)
            {
                EnterShort("MOMO_S");
                lastEntryBar = CurrentBar;
            }
        }

        private bool IsInTradeWindow(DateTime barTime)
        {
            // ToTime devuelve HHmmss (ej: 093000). Pasamos a HHmm dividiendo entre 100.
            int hhmm = ToTime(barTime) / 100;

            // Caso normal: 0900 -> 1700
            if (StartTimeHHmm <= EndTimeHHmm)
                return (hhmm >= StartTimeHHmm && hhmm <= EndTimeHHmm);

            // Caso overnight: 2000 -> 0300
            return (hhmm >= StartTimeHHmm || hhmm <= EndTimeHHmm);
        }

        private void DrawStatus(bool inWindow)
        {
            string tag = "MOMO_STATUS";
            string text = inWindow ? "MOMENTUM BOT: ACTIVO" : "MOMENTUM BOT: INACTIVO";
            Brush brush = inWindow ? Brushes.LimeGreen : Brushes.IndianRed;

            // Texto fijo arriba a la derecha
            Draw.TextFixed(
                this,
                tag,
                text,
                TextPosition.TopRight,
                brush,
                new SimpleFont("Arial", 16),
                Brushes.Transparent,
                Brushes.Transparent,
                0);
        }
    }
}
