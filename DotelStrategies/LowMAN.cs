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
    public class StreakGreenAfterRed_StopEntry : Strategy
    {
        private SMA sma;

        private Order entryOrder = null;
        private int lastSignalBar = -1;
        private int lastPrintedDate = -1;

        #region Inputs

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Green Streak (N)", Order = 1, GroupName = "01 - Pattern")]
        public int GreenStreak { get; set; } = 3;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Red Streak Before (M)", Order = 2, GroupName = "01 - Pattern")]
        public int RedStreakBefore { get; set; } = 5;

        // Entry offset
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Entry Offset (ticks) above Close", Order = 3, GroupName = "01 - Pattern")]
        public int EntryOffsetTicks { get; set; } = 1;

        // ----- Time filter -----
        [NinjaScriptProperty]
        [Display(Name = "Use Time Filter", Order = 10, GroupName = "02 - Time Filter")]
        public bool UseTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Start Time (HHmm)", Order = 11, GroupName = "02 - Time Filter")]
        public int StartTimeHHmm { get; set; } = 1530;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "End Time (HHmm)", Order = 12, GroupName = "02 - Time Filter")]
        public int EndTimeHHmm { get; set; } = 1730;

        // ----- SMA filter -----
        public enum SmaMode { PriceAboveSma, PriceBelowSma }

        [NinjaScriptProperty]
        [Display(Name = "Use SMA Filter", Order = 20, GroupName = "03 - SMA Filter")]
        public bool UseSmaFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "SMA Period", Order = 21, GroupName = "03 - SMA Filter")]
        public int SmaPeriod { get; set; } = 200;

        [NinjaScriptProperty]
        [Display(Name = "SMA Mode", Order = 22, GroupName = "03 - SMA Filter")]
        public SmaMode SmaFilterMode { get; set; } = SmaMode.PriceAboveSma;

        // ----- Risk (ticks) -----
        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Stop Loss (ticks)", Order = 30, GroupName = "04 - Risk")]
        public int StopLossTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Take Profit (ticks)", Order = 31, GroupName = "04 - Risk")]
        public int TakeProfitTicks { get; set; } = 40;

        // ----- Extras -----
        [NinjaScriptProperty]
        [Display(Name = "Cancel Entry On New Bar", Order = 40, GroupName = "05 - Extras")]
        public bool CancelEntryOnNewBar { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Print Date Each Day (Backtest)", Order = 41, GroupName = "05 - Extras")]
        public bool PrintDailyDate { get; set; } = true;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "StreakGreenAfterRed_StopEntry";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                StartBehavior = StartBehavior.WaitUntilFlat;

                BarsRequiredToTrade = 20;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                sma = SMA(SmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            int neededBars = GreenStreak + RedStreakBefore;
            if (CurrentBar < Math.Max(BarsRequiredToTrade, neededBars))
                return;

            if (PrintDailyDate)
                PrintDateOncePerDay();

            // (opcional) cancelar orden si no se ejecutó en la vela siguiente
            if (CancelEntryOnNewBar && entryOrder != null && entryOrder.OrderState == OrderState.Working)
                CancelOrder(entryOrder);

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (entryOrder != null && entryOrder.OrderState == OrderState.Working)
                return;

            if (UseTimeFilter && !IsWithinTimeWindow())
                return;

            if (UseSmaFilter && !PassesSmaFilter())
                return;

            if (lastSignalBar == CurrentBar)
                return;

            bool patternOk = IsGreenStreak(0, GreenStreak) && IsRedStreak(GreenStreak, RedStreakBefore);

            if (patternOk)
            {
                lastSignalBar = CurrentBar;

                double stopPrice = Close[0] + (EntryOffsetTicks * TickSize);
                stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

                // STOP MARKET X ticks por encima del cierre de la vela señal
                entryOrder = EnterLongStopMarket(0, true, 1, stopPrice, "L_Stop_GreenAfterRed");
            }
        }

        protected override void OnOrderUpdate(
            Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null || entryOrder == null)
                return;

            if (order.OrderId == entryOrder.OrderId)
            {
                if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                    entryOrder = null;
            }
        }

        // =========================
        // Pattern helpers
        // =========================

        private bool IsGreenStreak(int offsetStart, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int idx = offsetStart + i;
                if (!(Close[idx] > Open[idx]))
                    return false;
            }
            return true;
        }

        private bool IsRedStreak(int offsetStart, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int idx = offsetStart + i;
                if (!(Close[idx] < Open[idx]))
                    return false;
            }
            return true;
        }

        // =========================
        // Filters / extras
        // =========================

        private bool IsWithinTimeWindow()
        {
            int t = ToTime(Time[0]); // HHmmss
            int start = StartTimeHHmm * 100;
            int end = EndTimeHHmm * 100;

            if (StartTimeHHmm <= EndTimeHHmm)
                return t >= start && t <= end;

            return (t >= start) || (t <= end);
        }

        private bool PassesSmaFilter()
        {
            double s = sma[0];
            if (SmaFilterMode == SmaMode.PriceAboveSma)
                return Close[0] >= s;

            return Close[0] <= s;
        }

        private void PrintDateOncePerDay()
        {
            int d = ToDay(Time[0]);
            if (d == lastPrintedDate)
                return;

            lastPrintedDate = d;
            Print($"Backtest day: {Time[0]:yyyy-MM-dd}");
        }
    }
}
