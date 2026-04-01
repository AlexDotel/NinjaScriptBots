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

namespace NinjaTrader.NinjaScript.Strategies
{
    public class DonchianFalseBreakout : Strategy
    {
        private double donchianHighPrev;
        private double donchianLowPrev;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                                    = "DonchianFalseBreakout";
                Description                             = "Estrategia simple de scalping basada en false breakout del canal Donchian.";
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
                IsInstantiatedOnEachOptimizationIteration = false;

                DonchianPeriod                          = 20;
                OffsetTicks                             = 1;
                StopLossTicks                           = 6;
                TakeProfitTicks                         = 8;

                EnableLongs                             = true;
                EnableShorts                            = true;

                UseTimeFilter                           = false;
                StartTime                               = 93000;   // 09:30:00
                EndTime                                 = 113000;  // 11:30:00
            }
            else if (State == State.Configure)
            {
                SetStopLoss("LongEntry", CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget("LongEntry", CalculationMode.Ticks, TakeProfitTicks);

                SetStopLoss("ShortEntry", CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget("ShortEntry", CalculationMode.Ticks, TakeProfitTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(BarsRequiredToTrade, DonchianPeriod + 1))
                return;

            if (UseTimeFilter && !IsWithinTradingWindow())
                return;

            // Canal Donchian de la barra anterior, ya confirmado
            donchianHighPrev = MAX(High, DonchianPeriod)[1];
            donchianLowPrev  = MIN(Low, DonchianPeriod)[1];

            double offsetPrice = OffsetTicks * TickSize;

            bool longSignal =
                EnableLongs &&
                Position.MarketPosition == MarketPosition.Flat &&
                Low[0] <= donchianLowPrev - offsetPrice &&
                Close[0] > donchianLowPrev;

            bool shortSignal =
                EnableShorts &&
                Position.MarketPosition == MarketPosition.Flat &&
                High[0] >= donchianHighPrev + offsetPrice &&
                Close[0] < donchianHighPrev;

            if (longSignal)
                EnterLong(1, "LongEntry");

            if (shortSignal)
                EnterShort(1, "ShortEntry");
        }

        private bool IsWithinTradingWindow()
        {
            int currentTime = ToTime(Time[0]);

            // Ventana normal: por ejemplo 09:30 -> 11:30
            if (StartTime <= EndTime)
                return currentTime >= StartTime && currentTime <= EndTime;

            // Ventana cruzando medianoche: por ejemplo 23:00 -> 03:00
            return currentTime >= StartTime || currentTime <= EndTime;
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "DonchianPeriod", GroupName = "Parameters", Order = 0)]
        public int DonchianPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "OffsetTicks", GroupName = "Parameters", Order = 1)]
        public int OffsetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "StopLossTicks", GroupName = "Parameters", Order = 2)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "TakeProfitTicks", GroupName = "Parameters", Order = 3)]
        public int TakeProfitTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableLongs", GroupName = "Trade Filters", Order = 4)]
        public bool EnableLongs
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableShorts", GroupName = "Trade Filters", Order = 5)]
        public bool EnableShorts
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "UseTimeFilter", GroupName = "Time Filter", Order = 6)]
        public bool UseTimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "StartTime", GroupName = "Time Filter", Order = 7, Description = "Formato HHmmss, por ejemplo 093000")]
        public int StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "EndTime", GroupName = "Time Filter", Order = 8, Description = "Formato HHmmss, por ejemplo 113000")]
        public int EndTime
        { get; set; }

        #endregion
    }
}