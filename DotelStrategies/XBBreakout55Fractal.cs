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
    public class XB_Breakout_55Fractal_XBWindow : Strategy
    {
        // ====== Inputs ======
        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "Strength (bars left/right)", Order = 1, GroupName = "XB")]
        public int Strength { get; set; } = 55;

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "XB Window (bars) - how long to keep the level", Order = 2, GroupName = "XB")]
        public int XBWindowBars { get; set; } = 55;

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "StopLoss (ticks)", Order = 3, GroupName = "Risk")]
        public int StopLossTicks { get; set; } = 100;

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "TakeProfit (ticks)", Order = 4, GroupName = "Risk")]
        public int TakeProfitTicks { get; set; } = 100;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmm)", Order = 5, GroupName = "Time Filter")]
        public int StartTimeHHmm { get; set; } = 1530;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmm)", Order = 6, GroupName = "Time Filter")]
        public int EndTimeHHmm { get; set; } = 1730;

        [NinjaScriptProperty]
        [Display(Name = "Enable Longs", Order = 7, GroupName = "Direction")]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", Order = 8, GroupName = "Direction")]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Invert Trades (Break High=Short, Break Low=Long)", Order = 9, GroupName = "Direction")]
        public bool InvertTrades { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Draw Levels", Order = 10, GroupName = "Visual")]
        public bool DrawLevels { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Draw Entry/TP/SL Lines", Order = 11, GroupName = "Visual")]
        public bool DrawTradeLines { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "One Trade At A Time", Order = 12, GroupName = "Execution")]
        public bool OneTradeAtATime { get; set; } = true;

        // ====== State ======
        private DateTime lastPrintedDate = Core.Globals.MinDate;

        // Active level (only valid for XBWindowBars after creation)
        private string activeHighTag = null;
        private string activeLowTag  = null;

        private double activeHighPrice = double.NaN;
        private double activeLowPrice  = double.NaN;

        private int activeHighStartBar = -1; // CurrentBar when level activated
        private int activeLowStartBar  = -1;

        private double lastBrokenHigh = double.NaN;
        private double lastBrokenLow  = double.NaN;

        private int tradeDrawCounter = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "XB_Breakout_55Fractal_XBWindow";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < (Strength * 2 + 2))
                return;

            PrintDayProgress();

            if (!IsWithinTradingWindow(Time[0]))
                return;

            // Expire old levels automatically
            ExpireLevelsIfNeeded();

            // Confirm swings (55 left + 55 right)
            TryActivateNewLevelsFromConfirmedSwings();

            if (OneTradeAtATime && Position.MarketPosition != MarketPosition.Flat)
                return;

            // Breakout
            TryBreakoutEntries();
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (!DrawTradeLines)
                return;

            if (execution?.Order == null)
                return;

            string sig = execution.Order.Name ?? string.Empty;

            bool isEntrySignal =
                sig == "XB_BreakHigh_Long" ||
                sig == "XB_BreakLow_Long"  ||
                sig == "XB_BreakHigh_Short"||
                sig == "XB_BreakLow_Short";

            if (!isEntrySignal)
                return;

            tradeDrawCounter++;

            bool isLong = execution.Order.OrderAction == OrderAction.Buy;
            bool isShort = execution.Order.OrderAction == OrderAction.SellShort;
            if (!isLong && !isShort)
                return;

            double entry = execution.Price;
            double tp = entry + (isLong ? +TakeProfitTicks : -TakeProfitTicks) * TickSize;
            double sl = entry + (isLong ? -StopLossTicks : +StopLossTicks) * TickSize;

            string baseTag = $"XB_TRADE_{time:yyyyMMdd_HHmmss}_{tradeDrawCounter}";

            Draw.HorizontalLine(this, baseTag + "_ENTRY", entry, Brushes.Gold);
            Draw.HorizontalLine(this, baseTag + "_TP", tp, Brushes.LimeGreen);
            Draw.HorizontalLine(this, baseTag + "_SL", sl, Brushes.OrangeRed);
            Draw.VerticalLine(this, baseTag + "_V", 0, Brushes.DimGray);
        }

        private void PrintDayProgress()
        {
            if (Bars.IsFirstBarOfSession)
            {
                DateTime d = Time[0].Date;
                if (d != lastPrintedDate)
                {
                    Print($"[XB] Backtest day: {d:yyyy-MM-dd}");
                    lastPrintedDate = d;
                }
            }
        }

        private bool IsWithinTradingWindow(DateTime barTime)
        {
            int start = StartTimeHHmm * 100;
            int end   = EndTimeHHmm * 100;
            int now   = ToTime(barTime);

            if (start <= end)
                return now >= start && now <= end;

            return now >= start || now <= end;
        }

        private void ExpireLevelsIfNeeded()
        {
            if (!double.IsNaN(activeHighPrice) && activeHighStartBar >= 0)
            {
                if (CurrentBar - activeHighStartBar >= XBWindowBars)
                    ClearActiveHighLevel();
            }

            if (!double.IsNaN(activeLowPrice) && activeLowStartBar >= 0)
            {
                if (CurrentBar - activeLowStartBar >= XBWindowBars)
                    ClearActiveLowLevel();
            }
        }

        private void TryActivateNewLevelsFromConfirmedSwings()
        {
            int candidateAgo = Strength;

            // Activate High level
            if (IsConfirmedSwingHigh(candidateAgo))
            {
                double price = High[candidateAgo];
                ActivateHighLevel(price);
            }

            // Activate Low level
            if (IsConfirmedSwingLow(candidateAgo))
            {
                double price = Low[candidateAgo];
                ActivateLowLevel(price);
            }
        }

        private void ActivateHighLevel(double price)
        {
            if (DrawLevels && !string.IsNullOrEmpty(activeHighTag))
                RemoveDrawObject(activeHighTag);

            activeHighPrice = price;
            activeHighStartBar = CurrentBar;
            activeHighTag = $"XB_HIGH_{CurrentBar}";

            if (DrawLevels)
                Draw.Ray(this, activeHighTag, 0, price, 1, price, Brushes.DodgerBlue);

            // Reset anti-duplicate break guard for new level
            lastBrokenHigh = double.NaN;
        }

        private void ActivateLowLevel(double price)
        {
            if (DrawLevels && !string.IsNullOrEmpty(activeLowTag))
                RemoveDrawObject(activeLowTag);

            activeLowPrice = price;
            activeLowStartBar = CurrentBar;
            activeLowTag = $"XB_LOW_{CurrentBar}";

            if (DrawLevels)
                Draw.Ray(this, activeLowTag, 0, price, 1, price, Brushes.OrangeRed);

            lastBrokenLow = double.NaN;
        }

        private bool IsConfirmedSwingHigh(int candidateAgo)
        {
            double candidateHigh = High[candidateAgo];

            for (int i = 0; i < Strength; i++)
                if (High[i] >= candidateHigh)
                    return false;

            for (int i = 1; i <= Strength; i++)
                if (High[candidateAgo + i] > candidateHigh)
                    return false;

            return true;
        }

        private bool IsConfirmedSwingLow(int candidateAgo)
        {
            double candidateLow = Low[candidateAgo];

            for (int i = 0; i < Strength; i++)
                if (Low[i] <= candidateLow)
                    return false;

            for (int i = 1; i <= Strength; i++)
                if (Low[candidateAgo + i] < candidateLow)
                    return false;

            return true;
        }

        private void TryBreakoutEntries()
        {
            // HIGH breakout
            if (!double.IsNaN(activeHighPrice))
            {
                bool brokeUp = Close[1] <= activeHighPrice && Close[0] > activeHighPrice;

                if (brokeUp && (double.IsNaN(lastBrokenHigh) || Math.Abs(lastBrokenHigh - activeHighPrice) > TickSize * 0.5))
                {
                    lastBrokenHigh = activeHighPrice;

                    // remove level as soon as broken
                    ClearActiveHighLevel();

                    if (!InvertTrades)
                    {
                        if (EnableLongs)
                            EnterLong("XB_BreakHigh_Long");
                    }
                    else
                    {
                        if (EnableShorts)
                            EnterShort("XB_BreakHigh_Short");
                    }
                }
            }

            // LOW breakout
            if (!double.IsNaN(activeLowPrice))
            {
                bool brokeDown = Close[1] >= activeLowPrice && Close[0] < activeLowPrice;

                if (brokeDown && (double.IsNaN(lastBrokenLow) || Math.Abs(lastBrokenLow - activeLowPrice) > TickSize * 0.5))
                {
                    lastBrokenLow = activeLowPrice;

                    ClearActiveLowLevel();

                    if (!InvertTrades)
                    {
                        if (EnableShorts)
                            EnterShort("XB_BreakLow_Short");
                    }
                    else
                    {
                        if (EnableLongs)
                            EnterLong("XB_BreakLow_Long");
                    }
                }
            }
        }

        private void ClearActiveHighLevel()
        {
            if (!string.IsNullOrEmpty(activeHighTag))
                RemoveDrawObject(activeHighTag);

            activeHighTag = null;
            activeHighPrice = double.NaN;
            activeHighStartBar = -1;
        }

        private void ClearActiveLowLevel()
        {
            if (!string.IsNullOrEmpty(activeLowTag))
                RemoveDrawObject(activeLowTag);

            activeLowTag = null;
            activeLowPrice = double.NaN;
            activeLowStartBar = -1;
        }
    }
}
