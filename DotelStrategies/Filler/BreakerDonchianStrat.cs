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
    public class Breaker : Strategy
    {
        private DonchianChannel donchian;

        #region Parámetros

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Donchian Period", GroupName = "01. Parámetros", Order = 0)]
        public int DonchianPeriod { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "01. Parámetros", Order = 1)]
        public int StopLossTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit (ticks)", GroupName = "01. Parámetros", Order = 2)]
        public int TakeProfitTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Permitir Compras", GroupName = "01. Parámetros", Order = 3)]
        public bool AllowLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Permitir Ventas", GroupName = "01. Parámetros", Order = 4)]
        public bool AllowShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Hora Inicio (HHmm)", GroupName = "01. Parámetros", Order = 5)]
        public int StartTime { get; set; } = 930;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Hora Fin (HHmm)", GroupName = "01. Parámetros", Order = 6)]
        public int EndTime { get; set; } = 1600;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                                    = "Breaker";
                Description                             = "Estrategia breakout con Donchian Channel nativo.";
                Calculate                               = Calculate.OnBarClose;
                EntriesPerDirection                     = 1;
                EntryHandling                           = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy            = true;
                ExitOnSessionCloseSeconds               = 30;
                IsFillLimitOnTouch                      = false;
                MaximumBarsLookBack                     = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                     = OrderFillResolution.Standard;
                Slippage                                = 0;
                StartBehavior                           = StartBehavior.WaitUntilFlat;
                TimeInForce                             = TimeInForce.Gtc;
                TraceOrders                             = false;
                RealtimeErrorHandling                   = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                      = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                     = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                donchian = DonchianChannel(DonchianPeriod);
                AddChartIndicator(donchian);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(BarsRequiredToTrade, DonchianPeriod))
                return;

            if (!IsWithinTradingHours())
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double upperBandPrev = donchian.Upper[1];
            double lowerBandPrev = donchian.Lower[1];

            bool longBreakout  = Close[0] > upperBandPrev;
            bool shortBreakout = Close[0] < lowerBandPrev;

            if (AllowLongs && longBreakout)
            {
                EnterLong("BreakerLong");
            }
            else if (AllowShorts && shortBreakout)
            {
                EnterShort("BreakerShort");
            }
        }

        private bool IsWithinTradingHours()
        {
            int currentTime = ToTime(Time[0]) / 100;

            if (StartTime <= EndTime)
                return currentTime >= StartTime && currentTime <= EndTime;

            return currentTime >= StartTime || currentTime <= EndTime;
        }
    }
}