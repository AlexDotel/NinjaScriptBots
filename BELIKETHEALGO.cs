#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class BeLikeAlgo : Strategy
    {
        private MAX donchianHigh;
        private MIN donchianLow;

        private double lastHigh;
        private double lastLow;

        // NUEVO: flags para esperar la vela contraria
        private bool waitingBearAfterHigh; // se tomó high, espero primera vela bajista para short
        private bool waitingBullAfterLow;  // se tomó low,  espero primera vela alcista para long

        #region Inputs

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Donchian Period", Order=1)]
        public int DonchianPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Longs", Order=2)]
        public bool EnableLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Shorts", Order=3)]
        public bool EnableShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use FVG Filter", Order=4)]
        public bool UseFVG { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Stop Loss (ticks)", Order=5)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Take Profit (ticks)", Order=6)]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Start Time (HHMM)", Order=7)]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name="End Time (HHMM)", Order=8)]
        public int EndTime { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BeLikeAlgo";
                Calculate = Calculate.OnBarClose;

                DonchianPeriod = 20;
                EnableLongs = true;
                EnableShorts = true;
                UseFVG = false;

                StopLossTicks = 40;
                TakeProfitTicks = 80;

                StartTime = 930;
                EndTime = 2330;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
            }
            else if (State == State.DataLoaded)
            {
                donchianHigh = MAX(High, DonchianPeriod);
                donchianLow  = MIN(Low, DonchianPeriod);

                waitingBearAfterHigh = false;
                waitingBullAfterLow  = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < DonchianPeriod)
                return;

            if (!IsWithinTime())
                return;

            if (StopLossTicks <= 0 || TakeProfitTicks <= 0)
                return;

            // Si ya hay posición abierta, no buscamos nuevas señales y reseteamos espera
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                waitingBearAfterHigh = false;
                waitingBullAfterLow  = false;
                return;
            }

            // Donchian del bar anterior (sin look-ahead)
            lastHigh = donchianHigh[1];
            lastLow  = donchianLow[1];

            bool tookHigh = High[0] >= lastHigh;
            bool tookLow  = Low[0] <= lastLow;

            // 1) Detecto "toma" del high/low y activo modo espera
            if (tookHigh)
            {
                waitingBearAfterHigh = true;
                waitingBullAfterLow  = false; // prioriza el último evento
                Print($"{Time[0]} Took HIGH -> waiting for first bearish candle to SHORT");
            }
            else if (tookLow)
            {
                waitingBullAfterLow  = true;
                waitingBearAfterHigh = false;
                Print($"{Time[0]} Took LOW -> waiting for first bullish candle to LONG");
            }

            // 2) Confirmación: primera vela contraria cerrada
            bool isBearishCandle = Close[0] < Open[0];
            bool isBullishCandle = Close[0] > Open[0];

            // SHORT tras toma de HIGH + primera vela bajista
            if (waitingBearAfterHigh && EnableShorts && isBearishCandle)
            {
                if (UseFVG && !IsBearishFVG())
                    return;

                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);

                waitingBearAfterHigh = false;

                EnterShort("BL_Short");
                Print($"{Time[0]} CONFIRMED SHORT (bear candle after high) | SL={StopLossTicks}t TP={TakeProfitTicks}t");
                return;
            }

            // LONG tras toma de LOW + primera vela alcista
            if (waitingBullAfterLow && EnableLongs && isBullishCandle)
            {
                if (UseFVG && !IsBullishFVG())
                    return;

                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);

                waitingBullAfterLow = false;

                EnterLong("BL_Long");
                Print($"{Time[0]} CONFIRMED LONG (bull candle after low) | SL={StopLossTicks}t TP={TakeProfitTicks}t");
                return;
            }
        }

        #region Helpers

        // HHMM (ej: 0930, 2330). Soporta cruce de medianoche.
        private bool IsWithinTime()
        {
            int cur = ToTime(Time[0]);   // HHmmss
            int st  = StartTime * 100;   // HHmm00
            int en  = EndTime * 100;

            if (st <= en)
                return cur >= st && cur <= en;

            return cur >= st || cur <= en;
        }

        // FVG simple (3 velas)
        private bool IsBullishFVG()
        {
            if (CurrentBar < 3) return false;
            return Low[1] > High[2];
        }

        private bool IsBearishFVG()
        {
            if (CurrentBar < 3) return false;
            return High[1] < Low[2];
        }

        #endregion
    }
}