#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class GapOpenBotV1 : Strategy
    {
        // =========================
        // Variables internas
        // =========================
        private double currentSessionOpen = 0.0;
        private double previousSessionClose = 0.0;

        private bool sessionValuesReady = false;
        private bool gapDetected = false;
        private int gapDirection = 0; // 1 = gap alcista, -1 = gap bajista, 0 = sin gap
        private bool tradedToday = false;

        private DateTime currentSessionDate = Core.Globals.MinDate;
        private DateTime entryTimeLocalForToday = Core.Globals.MinDate;

        // =========================
        // OnStateChange
        // =========================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "GapOpenBotV1";
                Description = "Opera a favor del gap de apertura del día, con hora de entrada en formato decimal tipo España.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                // Inputs por defecto
                EntryTimeSpain = 9.00;
                AllowLongs = true;
                AllowShorts = true;
                StopLossTicks = 20;
                TakeProfitTicks = 40;
                MinGapTicks = 1;
            }
            else if (State == State.Configure)
            {
            }
        }

        // =========================
        // OnBarUpdate
        // =========================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            // Solo operar en la serie principal
            if (BarsInProgress != 0)
                return;

            // Detectar inicio de nueva sesión
            if (Bars.IsFirstBarOfSession)
            {
                ResetSessionState();

                currentSessionDate = Time[0].Date;
                currentSessionOpen = Open[0];
                previousSessionClose = Close[1];

                sessionValuesReady = true;

                DetectGap();

                entryTimeLocalForToday = ConvertSpanishDecimalHourToLocalDateTime(Time[0], EntryTimeSpain);
            }

            if (!sessionValuesReady)
                return;

            if (tradedToday)
                return;

            if (!gapDetected)
                return;

            // Esperar a que llegue la hora configurada
            if (Time[0] < entryTimeLocalForToday)
                return;

            // Solo una entrada por día
            TryEnterTrade();
        }

        // =========================
        // Lógica principal
        // =========================
        private void ResetSessionState()
        {
            sessionValuesReady = false;
            gapDetected = false;
            gapDirection = 0;
            tradedToday = false;
            currentSessionOpen = 0.0;
            previousSessionClose = 0.0;
            currentSessionDate = Core.Globals.MinDate;
            entryTimeLocalForToday = Core.Globals.MinDate;
        }

        private void DetectGap()
        {
            double gapSize = currentSessionOpen - previousSessionClose;
            double minGapPrice = MinGapTicks * TickSize;

            if (Math.Abs(gapSize) < minGapPrice)
            {
                gapDetected = false;
                gapDirection = 0;
                return;
            }

            if (gapSize > 0)
            {
                gapDetected = true;
                gapDirection = 1;
            }
            else if (gapSize < 0)
            {
                gapDetected = true;
                gapDirection = -1;
            }
        }

        private void TryEnterTrade()
        {
            // Configurar SL/TP antes de entrar
            SetStopLoss(CalculationMode.Ticks, StopLossTicks);
            SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);

            // Gap alcista -> compra
            if (gapDirection == 1 && AllowLongs && Position.MarketPosition == MarketPosition.Flat)
            {
                EnterLong(1, "GapLong");
                tradedToday = true;
                return;
            }

            // Gap bajista -> venta
            if (gapDirection == -1 && AllowShorts && Position.MarketPosition == MarketPosition.Flat)
            {
                EnterShort(1, "GapShort");
                tradedToday = true;
                return;
            }
        }

        // =========================
        // Conversión horaria
        // =========================
        private DateTime ConvertSpanishDecimalHourToLocalDateTime(DateTime referenceBarTime, double spanishHourDecimal)
        {
            ValidateQuarterHourInput(spanishHourDecimal);

            int totalMinutes = DecimalHourToMinutes(spanishHourDecimal);
            int hourSpain = totalMinutes / 60;
            int minuteSpain = totalMinutes % 60;

            // Zona horaria de España peninsular
            TimeZoneInfo spainZone = GetSpainTimeZone();
            TimeZoneInfo localZone = TimeZoneInfo.Local;

            // Tomamos la fecha del bar y construimos esa fecha con la hora española
            DateTime unspecifiedSpainDateTime = new DateTime(
                referenceBarTime.Year,
                referenceBarTime.Month,
                referenceBarTime.Day,
                hourSpain,
                minuteSpain,
                0,
                DateTimeKind.Unspecified
            );

            DateTime localConverted = TimeZoneInfo.ConvertTime(unspecifiedSpainDateTime, spainZone, localZone);

            return localConverted;
        }

        private TimeZoneInfo GetSpainTimeZone()
        {
            try
            {
                // Windows
                return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
            }
            catch
            {
                try
                {
                    // Algunos sistemas podrían exponer esta ID
                    return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
                }
                catch
                {
                    // Fallback
                    return TimeZoneInfo.Local;
                }
            }
        }

        private void ValidateQuarterHourInput(double hourValue)
        {
            if (hourValue < 0 || hourValue >= 24)
                throw new ArgumentException("La hora debe estar entre 0.00 y 23.75");

            double fractional = hourValue - Math.Floor(hourValue);

            bool validFraction =
                   Math.Abs(fractional - 0.00) < 0.0001
                || Math.Abs(fractional - 0.25) < 0.0001
                || Math.Abs(fractional - 0.50) < 0.0001
                || Math.Abs(fractional - 0.75) < 0.0001;

            if (!validFraction)
                throw new ArgumentException("La hora decimal solo admite fracciones .00, .25, .50 y .75");
        }

        private int DecimalHourToMinutes(double hourValue)
        {
            int wholeHour = (int)Math.Floor(hourValue);
            double fractional = hourValue - wholeHour;

            int minutes = 0;

            if (Math.Abs(fractional - 0.25) < 0.0001)
                minutes = 15;
            else if (Math.Abs(fractional - 0.50) < 0.0001)
                minutes = 30;
            else if (Math.Abs(fractional - 0.75) < 0.0001)
                minutes = 45;

            return wholeHour * 60 + minutes;
        }

        // =========================
        // Inputs
        // =========================

        [NinjaScriptProperty]
        [Display(Name = "Entry Time Spain (decimal)", GroupName = "01. Horario", Order = 0)]
        [Range(0.00, 23.75)]
        public double EntryTimeSpain { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Longs", GroupName = "02. Filtros", Order = 0)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Shorts", GroupName = "02. Filtros", Order = 1)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss (ticks)", GroupName = "03. Riesgo", Order = 0)]
        [Range(1, int.MaxValue)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit (ticks)", GroupName = "03. Riesgo", Order = 1)]
        [Range(1, int.MaxValue)]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Min Gap (ticks)", GroupName = "04. Gap", Order = 0)]
        [Range(1, int.MaxValue)]
        public int MinGapTicks { get; set; }
    }
}