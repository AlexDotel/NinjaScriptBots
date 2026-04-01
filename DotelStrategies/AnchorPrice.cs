#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class AnchorPrice : Strategy
    {
        // ================= INPUTS =================
        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "CheckTime (HHmm)", Order = 1, GroupName = "Parameters")]
        public int CheckTimeHHmm { get; set; } = 1630;   // por defecto 16:30

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "AnchorTime (HHmm)", Order = 2, GroupName = "Parameters")]
        public int AnchorTimeHHmm { get; set; } = 1530;  // por defecto 15:30

        [NinjaScriptProperty]
        [Range(0.01, 10.0)]
        [Display(Name = "StopLossMultiplier", Description = "0.5 = SL mitad (RR 2:1), 1.0 = 1:1, 2.0 = SL doble.", Order = 3, GroupName = "Parameters")]
        public double StopLossMultiplier { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Range(1.0, 1000000.0)]
        [Display(Name = "RiskDollars", Description = "Riesgo máximo aproximado en USD para el SL (se ajusta por MaxContracts).", Order = 4, GroupName = "Risk")]
        public double RiskDollars { get; set; } = 500.0;

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "MaxContracts", Description = "Máximo de contratos permitidos. La cantidad calculada se capea aquí.", Order = 5, GroupName = "Risk")]
        public int MaxContracts { get; set; } = 7;

        [NinjaScriptProperty]
        [Display(Name = "EnableLongs", Order = 6, GroupName = "Parameters")]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "EnableShorts", Order = 7, GroupName = "Parameters")]
        public bool EnableShorts { get; set; } = true;

        // ================= STATE =================
        private double anchorOpenToday = double.NaN;
        private double anchorOpenPrevDay = double.NaN;

        private bool anchorCapturedToday = false;
        private int lastTradeDate = -1;

        private const string LongSignal  = "LONG_AnchorPrice";
        private const string ShortSignal = "SHORT_AnchorPrice";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AnchorPrice";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            int today = ToDay(Time[0]);

            // ===== Nueva sesión/día =====
            if (Bars.IsFirstBarOfSession)
            {
                if (!double.IsNaN(anchorOpenToday))
                    anchorOpenPrevDay = anchorOpenToday;

                anchorOpenToday = double.NaN;
                anchorCapturedToday = false;
            }

            // ===== Capturar apertura AnchorTime (una vez por sesión) =====
            if (!anchorCapturedToday && IsTimeMatch(Time[0], AnchorTimeHHmm))
            {
                anchorOpenToday = Open[0];
                anchorCapturedToday = true;
            }

            // ===== A la hora CheckTime: evaluar y operar (máx 1 trade/día) =====
            if (!IsTimeMatch(Time[0], CheckTimeHHmm))
                return;

            if (lastTradeDate == today)
                return;

            if (double.IsNaN(anchorOpenPrevDay))
                return;

            double price = Close[0];

            // LONG si precio < anchor
            if (EnableLongs && price < anchorOpenPrevDay)
            {
                double tp = anchorOpenPrevDay;
                double tpDist = tp - price;                  // >0
                if (tpDist <= 0)
                    return;

                double sl = price - (tpDist * StopLossMultiplier);

                int qty = CalculateQtyFromRisk(entryPrice: price, stopPrice: sl);
                if (qty <= 0)
                    return;

                SetProfitTarget(LongSignal, CalculationMode.Price, tp);
                SetStopLoss(LongSignal, CalculationMode.Price, sl, false);

                EnterLong(qty, LongSignal);
                lastTradeDate = today;
            }
            // SHORT si precio > anchor
            else if (EnableShorts && price > anchorOpenPrevDay)
            {
                double tp = anchorOpenPrevDay;
                double tpDist = price - tp;                  // >0
                if (tpDist <= 0)
                    return;

                double sl = price + (tpDist * StopLossMultiplier);

                int qty = CalculateQtyFromRisk(entryPrice: price, stopPrice: sl);
                if (qty <= 0)
                    return;

                SetProfitTarget(ShortSignal, CalculationMode.Price, tp);
                SetStopLoss(ShortSignal, CalculationMode.Price, sl, false);

                EnterShort(qty, ShortSignal);
                lastTradeDate = today;
            }
        }

        // ================= RISK / POSITION SIZING =================
        private int CalculateQtyFromRisk(double entryPrice, double stopPrice)
        {
            double stopDistPrice = Math.Abs(entryPrice - stopPrice);
            if (stopDistPrice <= 0)
                return 0;

            double tickSize = TickSize;
            if (tickSize <= 0)
                return 0;

            // ticks entre entrada y stop
            double stopTicks = stopDistPrice / tickSize;

            // valor por tick por 1 contrato
            double tickValue = Instrument.MasterInstrument.PointValue * tickSize;
            if (tickValue <= 0)
                return 0;

            double riskPerContract = stopTicks * tickValue;
            if (riskPerContract <= 0)
                return 0;

            int qty = (int)Math.Floor(RiskDollars / riskPerContract);

            // Si con 1 contrato ya te pasarías del riesgo, no entra (respeta RiskDollars)
            if (qty < 1)
                return 0;

            // Aproximación por máximo permitido
            if (qty > MaxContracts)
                qty = MaxContracts;

            return qty;
        }

        // ================= HELPERS =================
        private bool IsTimeMatch(DateTime time, int hhmm)
        {
            int hh = hhmm / 100;
            int mm = hhmm % 100;

            // HHmmss
            int target = (hh * 10000) + (mm * 100);
            return ToTime(time) == target;
        }
    }
}
