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
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class SMACROSS : Strategy
    {
        private SMA sma;
        private VWAP vwap;

        private TimeSpan startTimeSpan;
        private TimeSpan endTimeSpan;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SMACROSS";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                // Inputs
                Quantity = 1;
                MaPeriod = 50;

                EnableTimeFilter = true;
                StartHourDecimal = 9.0;
                EndHourDecimal = 17.0;

                AllowLong = true;
                AllowShort = true;

                StopLossTicks = 20;
                TakeProfitMultiplier = 2.0;
            }
            else if (State == State.Configure)
            {
                ValidateTimeInput(StartHourDecimal);
                ValidateTimeInput(EndHourDecimal);

                startTimeSpan = ConvertDecimalToTimeSpan(StartHourDecimal);
                endTimeSpan   = ConvertDecimalToTimeSpan(EndHourDecimal);
            }
            else if (State == State.DataLoaded)
            {
                sma = SMA(MaPeriod);
                vwap = VWAP();

                AddChartIndicator(sma);
                AddChartIndicator(vwap);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < MaPeriod)
                return;

            if (BarsInProgress != 0)
                return;

            // ---- Filtro horario ----
            if (EnableTimeFilter)
            {
                TimeSpan now = Times[0][0].TimeOfDay;

                if (startTimeSpan <= endTimeSpan)
                {
                    if (now < startTimeSpan || now > endTimeSpan)
                        return;
                }
                else
                {
                    if (now > endTimeSpan && now < startTimeSpan)
                        return;
                }
            }

            // ---- SL / TP ----
            int sl = Math.Max(1, StopLossTicks);
            int tp = (int)Math.Round(sl * TakeProfitMultiplier, MidpointRounding.AwayFromZero);
            tp = Math.Max(1, tp);

            SetStopLoss("Long", CalculationMode.Ticks, sl, false);
            SetProfitTarget("Long", CalculationMode.Ticks, tp);

            SetStopLoss("Short", CalculationMode.Ticks, sl, false);
            SetProfitTarget("Short", CalculationMode.Ticks, tp);

            // ---- Condiciones ----
            bool aboveVWAP = Close[0] > vwap[0];
            bool belowVWAP = Close[0] < vwap[0];

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (AllowLong && aboveVWAP && CrossAbove(Close, sma, 1))
                {
                    EnterLong(Quantity, "Long");
                }

                if (AllowShort && belowVWAP && CrossBelow(Close, sma, 1))
                {
                    EnterShort(Quantity, "Short");
                }
            }
        }

        // =========================
        // 🔹 Helpers
        // =========================

        private void ValidateTimeInput(double value)
        {
            if (value < 0 || value > 23.75 || (value * 100) % 25 != 0)
            {
                throw new ArgumentException("Hora inválida. Usa formato 0 - 23.75 en pasos de 0.25");
            }
        }

        private TimeSpan ConvertDecimalToTimeSpan(double value)
        {
            int hours = (int)value;
            int minutes = (int)Math.Round((value - hours) * 100);

            // Convertimos 25 → 15 min, 50 → 30 min, 75 → 45 min
            if (minutes == 25) minutes = 15;
            else if (minutes == 50) minutes = 30;
            else if (minutes == 75) minutes = 45;
            else if (minutes == 0) minutes = 0;

            return new TimeSpan(hours, minutes, 0);
        }

        // =========================
        // 🔹 Inputs
        // =========================

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Quantity", GroupName="Trade", Order=0)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name="SMA Period", GroupName="Indicators", Order=1)]
        public int MaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Time Filter", GroupName="Time", Order=2)]
        public bool EnableTimeFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Start Hour (decimal)", Description="Ej: 9.25 = 09:15", GroupName="Time", Order=3)]
        public double StartHourDecimal { get; set; }

        [NinjaScriptProperty]
        [Display(Name="End Hour (decimal)", Description="Ej: 16.75 = 16:45", GroupName="Time", Order=4)]
        public double EndHourDecimal { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Allow Long", GroupName="Filters", Order=5)]
        public bool AllowLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Allow Short", GroupName="Filters", Order=6)]
        public bool AllowShort { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name="Stop Loss (ticks)", GroupName="Risk", Order=7)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="TP Multiplier", GroupName="Risk", Order=8)]
        public double TakeProfitMultiplier { get; set; }
    }
}
