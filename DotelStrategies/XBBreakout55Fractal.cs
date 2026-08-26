#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public enum XBFractalPanelCorner
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight
    }

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
        public int StopLossTicks { get; set; } = 80;

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "TakeProfit (ticks)", Order = 4, GroupName = "Risk")]
        public int TakeProfitTicks { get; set; } = 160;

        [NinjaScriptProperty]
        [Display(Name = "Use Profit Target", Order = 5, GroupName = "Risk")]
        public bool UseProfitTarget { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Use Long Time Filter EST", Order = 5, GroupName = "Time Filter")]
        public bool UseTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Long Start Time EST (HHmm)", Order = 6, GroupName = "Time Filter")]
        public int StartTimeHHmm { get; set; } = 930;

        [NinjaScriptProperty]
        [Display(Name = "Long End Time EST (HHmm)", Order = 7, GroupName = "Time Filter")]
        public int EndTimeHHmm { get; set; } = 1130;

        [NinjaScriptProperty]
        [Display(Name = "Enable Longs", Order = 8, GroupName = "Direction")]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", Order = 9, GroupName = "Direction")]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Invert Trades (Break High=Short, Break Low=Long)", Order = 10, GroupName = "Direction")]
        public bool InvertTrades { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Use Exit After Bars", Order = 11, GroupName = "Exit")]
        public bool UseExitAfterBars { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Exit After Bars", Order = 12, GroupName = "Exit")]
        public int ExitAfterBars { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Fast Backtest Mode", Order = 13, GroupName = "Backtest")]
        public bool FastBacktestMode { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Draw Levels", Order = 14, GroupName = "Visual")]
        public bool DrawLevels { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Draw Entry/TP/SL Lines", Order = 15, GroupName = "Visual")]
        public bool DrawTradeLines { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "One Trade At A Time", Order = 16, GroupName = "Execution")]
        public bool OneTradeAtATime { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Order = 17, GroupName = "Risk")]
        public bool UseTrailingStop { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Trailing Stop (ticks)", Order = 18, GroupName = "Risk")]
        public int TrailingStopTicks { get; set; } = 80;

        [NinjaScriptProperty]
        [Display(Name = "Trail With Internal SuperTrend", Order = 19, GroupName = "Risk")]
        public bool TrailWithSuperTrend { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "SuperTrend ATR Period", Order = 20, GroupName = "Risk")]
        public int SuperTrendAtrPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "SuperTrend Multiplier", Order = 21, GroupName = "Risk")]
        public double SuperTrendMultiplier { get; set; } = 3.0;

        [NinjaScriptProperty]
        [Display(Name = "Show Chart Panel", Order = 22, GroupName = "Visual")]
        public bool ShowChartPanel { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Panel Corner", Order = 23, GroupName = "Visual")]
        public XBFractalPanelCorner PanelCorner { get; set; } = XBFractalPanelCorner.BottomLeft;

        // Active level (only valid for XBWindowBars after creation)
        private string activeHighTag = null;
        private string activeLowTag  = null;

        private double activeHighPrice = double.NaN;
        private double activeLowPrice  = double.NaN;

        private int activeHighStartBar = -1; // CurrentBar when level activated
        private int activeLowStartBar  = -1;
        private int activeHighOriginBar = -1;
        private int activeLowOriginBar = -1;

        private double lastBrokenHigh = double.NaN;
        private double lastBrokenLow  = double.NaN;

        private int tradeDrawCounter = 0;
        private TimeZoneInfo easternTimeZone;
        private int activeEntryBar = -1;
        private double activeEntryPrice = double.NaN;
        private MarketPosition trackedPosition = MarketPosition.Flat;
        private double mostFavorablePrice = double.NaN;
        private ATR superTrendAtr;
        private Swing swingIndicator;
        private bool superTrendInitialized;
        private double superTrendUpperBand = double.NaN;
        private double superTrendLowerBand = double.NaN;
        private double internalSuperTrend = double.NaN;
        private int superTrendDirection;
        private double activeSuperTrendStop = double.NaN;
        private string activeEntrySignal = null;
        private Grid statusPanelHost;
        private Border statusPanel;
        private Border statusBadge;
        private TextBlock statusText;
        private TextBlock windowText;
        private TextBlock levelsText;
        private TextBlock positionText;
        private TextBlock trailingText;
        private TextBlock riskText;
        private DispatcherTimer panelTimer;
        private readonly List<string> activeTradeDrawTags = new List<string>();

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
                TraceOrders = false;

                IsInstantiatedOnEachOptimizationIteration = false;

                Strength = 55;
                XBWindowBars = 55;
                StopLossTicks = 80;
                TakeProfitTicks = 160;
                UseProfitTarget = false;
                UseTimeFilter = true;
                StartTimeHHmm = 930;
                EndTimeHHmm = 1130;
                EnableLongs = true;
                EnableShorts = false;
                InvertTrades = false;
                UseExitAfterBars = false;
                ExitAfterBars = 10;
                FastBacktestMode = true;
                DrawLevels = true;
                DrawTradeLines = true;
                OneTradeAtATime = true;
                UseTrailingStop = true;
                TrailingStopTicks = 80;
                TrailWithSuperTrend = false;
                SuperTrendAtrPeriod = 14;
                SuperTrendMultiplier = 3.0;
                ShowChartPanel = true;
                PanelCorner = XBFractalPanelCorner.BottomLeft;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = Strength * 2 + 2;
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                ConfigureProtectiveOrders("XB_BreakHigh_Long");
                ConfigureProtectiveOrders("XB_BreakLow_Long");
                ConfigureProtectiveOrders("XB_BreakHigh_Short");
                ConfigureProtectiveOrders("XB_BreakLow_Short");
            }
            else if (State == State.DataLoaded)
            {
                superTrendAtr = ATR(SuperTrendAtrPeriod);
                swingIndicator = Swing(Strength);
                ResetRuntimeState();
            }
            else if (State == State.Historical)
            {
                CreateStatusPanel();
            }
            else if (State == State.Realtime)
            {
                RefreshStatusPanel();
            }
            else if (State == State.Terminated)
            {
                RemoveStatusPanel();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < (Strength * 2 + 2))
                return;

            UpdateTradeTracking();
            UpdateInternalSuperTrend();
            bool positionExited = ManageCloseBasedExits();

            if (positionExited)
                return;

            // Expire old levels automatically
            ExpireLevelsIfNeeded();

            // Confirm swings (55 left + 55 right)
            TryActivateNewLevelsFromConfirmedSwings();
            RefreshActiveLevelDrawings();

            if (OneTradeAtATime && Position.MarketPosition != MarketPosition.Flat)
                return;

            // Breakout
            TryBreakoutEntries();
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
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

            activeEntrySignal = sig;

            if (!ShouldDrawTradeLines())
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
            Draw.HorizontalLine(this, baseTag + "_SL", sl, Brushes.OrangeRed);
            Draw.VerticalLine(this, baseTag + "_V", 0, Brushes.DimGray);
            activeTradeDrawTags.Add(baseTag + "_ENTRY");
            activeTradeDrawTags.Add(baseTag + "_SL");
            activeTradeDrawTags.Add(baseTag + "_V");

            if (UseProfitTarget)
            {
                Draw.HorizontalLine(this, baseTag + "_TP", tp, Brushes.LimeGreen);
                activeTradeDrawTags.Add(baseTag + "_TP");
            }
        }

        private void UpdateTradeTracking()
        {
            MarketPosition currentPosition = Position.MarketPosition;

            if (currentPosition == MarketPosition.Flat)
            {
                if (trackedPosition != MarketPosition.Flat)
                {
                    if (!string.IsNullOrEmpty(activeEntrySignal))
                        ConfigureProtectiveOrders(activeEntrySignal);
                    ResetTradeTracking();
                }

                return;
            }

            if (trackedPosition != currentPosition || activeEntryBar < 0)
            {
                trackedPosition = currentPosition;
                activeEntryBar = CurrentBar;
                activeEntryPrice = Position.AveragePrice;
                mostFavorablePrice = activeEntryPrice;
                activeSuperTrendStop = double.NaN;
            }
        }

        private bool ManageCloseBasedExits()
        {
            if (Position.MarketPosition == MarketPosition.Flat || activeEntryBar < 0 || double.IsNaN(activeEntryPrice))
                return false;

            if (UseExitAfterBars && CurrentBar - activeEntryBar >= ExitAfterBars)
            {
                ExitCurrentPosition("XB_ExitBars");
                return true;
            }

            UpdateWorkingTrailingStop();
            return false;
        }

        private void ConfigureProtectiveOrders(string entrySignal)
        {
            SetStopLoss(entrySignal, CalculationMode.Ticks, StopLossTicks, false);
            if (UseProfitTarget)
                SetProfitTarget(entrySignal, CalculationMode.Ticks, TakeProfitTicks);
        }

        private void UpdateWorkingTrailingStop()
        {
            if (!UseTrailingStop || string.IsNullOrEmpty(activeEntrySignal))
                return;

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            if (isLong)
                mostFavorablePrice = Math.Max(mostFavorablePrice, High[0]);
            else
                mostFavorablePrice = Math.Min(mostFavorablePrice, Low[0]);

            double fixedStop = activeEntryPrice + (isLong ? -StopLossTicks : StopLossTicks) * TickSize;
            double candidate;

            if (TrailWithSuperTrend)
            {
                if (!superTrendInitialized || double.IsNaN(internalSuperTrend))
                    return;

                // Do not submit a stop on the wrong side of the market after a trend flip.
                if ((isLong && internalSuperTrend >= Close[0]) || (!isLong && internalSuperTrend <= Close[0]))
                    return;

                candidate = internalSuperTrend;
            }
            else
            {
                candidate = mostFavorablePrice + (isLong ? -TrailingStopTicks : TrailingStopTicks) * TickSize;
            }

            candidate = isLong ? Math.Max(fixedStop, candidate) : Math.Min(fixedStop, candidate);
            activeSuperTrendStop = double.IsNaN(activeSuperTrendStop)
                ? candidate
                : isLong ? Math.Max(activeSuperTrendStop, candidate) : Math.Min(activeSuperTrendStop, candidate);
            activeSuperTrendStop = Instrument.MasterInstrument.RoundToTickSize(activeSuperTrendStop);

            // This modifies the real managed stop order linked to the filled entry.
            SetStopLoss(activeEntrySignal, CalculationMode.Price, activeSuperTrendStop, false);
        }

        private void ExitCurrentPosition(string signalName)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(signalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(signalName);
        }

        private void ResetTradeTracking()
        {
            activeEntryBar = -1;
            activeEntryPrice = double.NaN;
            trackedPosition = MarketPosition.Flat;
            mostFavorablePrice = double.NaN;
            activeSuperTrendStop = double.NaN;
            activeEntrySignal = null;

            foreach (string tag in activeTradeDrawTags)
                RemoveDrawObject(tag);
            activeTradeDrawTags.Clear();
        }

        private void UpdateInternalSuperTrend()
        {
            double atrValue = superTrendAtr[0];
            double midpoint = (High[0] + Low[0]) * 0.5;
            double basicUpper = midpoint + SuperTrendMultiplier * atrValue;
            double basicLower = midpoint - SuperTrendMultiplier * atrValue;

            if (!superTrendInitialized)
            {
                superTrendUpperBand = basicUpper;
                superTrendLowerBand = basicLower;
                superTrendDirection = Close[0] >= midpoint ? 1 : -1;
                internalSuperTrend = superTrendDirection == 1 ? superTrendLowerBand : superTrendUpperBand;
                superTrendInitialized = true;
                return;
            }

            double previousUpper = superTrendUpperBand;
            double previousLower = superTrendLowerBand;

            superTrendUpperBand = basicUpper < previousUpper || Close[1] > previousUpper
                ? basicUpper
                : previousUpper;
            superTrendLowerBand = basicLower > previousLower || Close[1] < previousLower
                ? basicLower
                : previousLower;

            if (superTrendDirection == -1 && Close[0] > superTrendUpperBand)
                superTrendDirection = 1;
            else if (superTrendDirection == 1 && Close[0] < superTrendLowerBand)
                superTrendDirection = -1;

            internalSuperTrend = superTrendDirection == 1 ? superTrendLowerBand : superTrendUpperBand;
        }

        private void ResetRuntimeState()
        {
            activeHighTag = null;
            activeLowTag = null;
            activeHighPrice = double.NaN;
            activeLowPrice = double.NaN;
            activeHighStartBar = -1;
            activeLowStartBar = -1;
            activeHighOriginBar = -1;
            activeLowOriginBar = -1;
            lastBrokenHigh = double.NaN;
            lastBrokenLow = double.NaN;
            tradeDrawCounter = 0;
            superTrendInitialized = false;
            superTrendUpperBand = double.NaN;
            superTrendLowerBand = double.NaN;
            internalSuperTrend = double.NaN;
            superTrendDirection = 0;
            ResetTradeTracking();
        }

        private bool IsWithinTradingWindow(DateTime barTime)
        {
            if (!UseTimeFilter)
                return true;

            int start = StartTimeHHmm * 100;
            int end   = EndTimeHHmm * 100;
            DateTime estTime = TimeZoneInfo.ConvertTime(barTime, TimeZoneInfo.Local, easternTimeZone);
            int now = (estTime.Hour * 10000) + (estTime.Minute * 100) + estTime.Second;

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
            // NinjaTrader's Swing indicator confirms its pivot Strength bars later.
            // Only accept a pivot exactly Strength bars ago so the same historical
            // swing is not activated repeatedly on subsequent bars.
            int swingHighAgo = swingIndicator.SwingHighBar(0, 1, Strength + 1);
            if (swingHighAgo == Strength)
            {
                double price = High[swingHighAgo];
                ActivateHighLevel(price, swingHighAgo);
            }

            int swingLowAgo = swingIndicator.SwingLowBar(0, 1, Strength + 1);
            if (swingLowAgo == Strength)
            {
                double price = Low[swingLowAgo];
                ActivateLowLevel(price, swingLowAgo);
            }
        }

        private void ActivateHighLevel(double price, int candidateAgo)
        {
            if (ShouldDrawLevels() && !string.IsNullOrEmpty(activeHighTag))
                RemoveDrawObject(activeHighTag);

            activeHighPrice = price;
            activeHighStartBar = CurrentBar;
            activeHighOriginBar = CurrentBar - candidateAgo;
            activeHighTag = $"XB_HIGH_{CurrentBar}";

            // Reset anti-duplicate break guard for new level
            lastBrokenHigh = double.NaN;
        }

        private void ActivateLowLevel(double price, int candidateAgo)
        {
            if (ShouldDrawLevels() && !string.IsNullOrEmpty(activeLowTag))
                RemoveDrawObject(activeLowTag);

            activeLowPrice = price;
            activeLowStartBar = CurrentBar;
            activeLowOriginBar = CurrentBar - candidateAgo;
            activeLowTag = $"XB_LOW_{CurrentBar}";

            lastBrokenLow = double.NaN;
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
                        if (EnableLongs && IsWithinTradingWindow(Time[0]))
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
                        if (EnableLongs && IsWithinTradingWindow(Time[0]))
                            EnterLong("XB_BreakLow_Long");
                    }
                }
            }
        }

        private void ClearActiveHighLevel()
        {
            if (ShouldDrawLevels() && !string.IsNullOrEmpty(activeHighTag))
                RemoveDrawObject(activeHighTag);

            activeHighTag = null;
            activeHighPrice = double.NaN;
            activeHighStartBar = -1;
            activeHighOriginBar = -1;
        }

        private void ClearActiveLowLevel()
        {
            if (ShouldDrawLevels() && !string.IsNullOrEmpty(activeLowTag))
                RemoveDrawObject(activeLowTag);

            activeLowTag = null;
            activeLowPrice = double.NaN;
            activeLowStartBar = -1;
            activeLowOriginBar = -1;
        }

        private void RefreshActiveLevelDrawings()
        {
            if (!ShouldDrawLevels())
                return;

            // Levels are visible only while the configured New York window is open.
            if (!IsWithinTradingWindow(Time[0]))
            {
                if (!string.IsNullOrEmpty(activeHighTag))
                    RemoveDrawObject(activeHighTag);
                if (!string.IsNullOrEmpty(activeLowTag))
                    RemoveDrawObject(activeLowTag);
                return;
            }

            if (!double.IsNaN(activeHighPrice) && activeHighOriginBar >= 0)
            {
                int highStartBarsAgo = Math.Max(0, CurrentBar - activeHighOriginBar);
                Draw.Line(this, activeHighTag, false, highStartBarsAgo, activeHighPrice,
                    0, activeHighPrice, Brushes.DodgerBlue, DashStyleHelper.Solid, 2);
            }

            if (!double.IsNaN(activeLowPrice) && activeLowOriginBar >= 0)
            {
                int lowStartBarsAgo = Math.Max(0, CurrentBar - activeLowOriginBar);
                Draw.Line(this, activeLowTag, false, lowStartBarsAgo, activeLowPrice,
                    0, activeLowPrice, Brushes.OrangeRed, DashStyleHelper.Solid, 2);
            }
        }

        private void CreateStatusPanel()
        {
            if (!ShowChartPanel || ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (statusPanelHost != null && UserControlCollection.Contains(statusPanelHost))
                        return;

                    StackPanel content = new StackPanel { Orientation = Orientation.Vertical };
                    StackPanel header = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 13)
                    };

                    statusText = MakePanelText("INACTIVE", 12, FontWeights.Bold, Brushes.White);
                    statusBadge = new Border
                    {
                        Background = MakePanelBrush(71, 85, 105),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(10, 4, 10, 4),
                        Child = statusText
                    };
                    header.Children.Add(statusBadge);

                    TextBlock brand = MakePanelText("XB FRACTAL", 13, FontWeights.SemiBold,
                        MakePanelBrush(148, 163, 184));
                    brand.Margin = new Thickness(10, 4, 0, 0);
                    header.Children.Add(brand);
                    content.Children.Add(header);

                    TextBlock caption = MakePanelText("LONG TRADING WINDOW · NEW YORK", 10,
                        FontWeights.SemiBold, MakePanelBrush(100, 116, 139));
                    content.Children.Add(caption);
                    windowText = MakePanelText("--:-- – --:--", 22, FontWeights.Bold, Brushes.White);
                    windowText.Margin = new Thickness(0, 2, 0, 12);
                    content.Children.Add(windowText);
                    content.Children.Add(MakePanelDivider());

                    levelsText = MakePanelText("", 12, FontWeights.SemiBold, MakePanelBrush(125, 211, 252));
                    positionText = MakePanelText("", 12, FontWeights.SemiBold, MakePanelBrush(203, 213, 225));
                    trailingText = MakePanelText("", 12, FontWeights.SemiBold, MakePanelBrush(250, 204, 21));
                    riskText = MakePanelText("", 12, FontWeights.SemiBold, MakePanelBrush(134, 239, 172));
                    content.Children.Add(MakePanelMetricCard(levelsText, MakePanelBrush(12, 74, 110)));
                    content.Children.Add(MakePanelMetricCard(positionText, MakePanelBrush(30, 41, 59)));
                    content.Children.Add(MakePanelMetricCard(trailingText, MakePanelBrush(113, 63, 18)));
                    content.Children.Add(MakePanelMetricCard(riskText, MakePanelBrush(20, 83, 45)));

                    statusPanel = new Border
                    {
                        Background = MakePanelBrush(10, 16, 28),
                        BorderBrush = MakePanelBrush(51, 65, 85),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(16),
                        Width = 370,
                        Child = content,
                        Effect = new DropShadowEffect
                        {
                            BlurRadius = 18,
                            ShadowDepth = 3,
                            Opacity = 0.42,
                            Color = Colors.Black
                        }
                    };

                    statusPanelHost = new Grid
                    {
                        Name = "XBFractalStatusPanel",
                        IsHitTestVisible = false
                    };
                    statusPanelHost.Children.Add(statusPanel);
                    System.Windows.Controls.Panel.SetZIndex(statusPanelHost, 99999);
                    ApplyPanelCorner();
                    UserControlCollection.Add(statusPanelHost);

                    panelTimer = new DispatcherTimer(DispatcherPriority.Background,
                        ChartControl.Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
                    panelTimer.Tick += OnPanelTimerTick;
                    panelTimer.Start();
                    RefreshStatusPanelCore();
                }
                catch (Exception ex)
                {
                    Print("Error creating XB Fractal panel: " + ex.Message);
                }
            });
        }

        private void OnPanelTimerTick(object sender, EventArgs e)
        {
            RefreshStatusPanelCore();
        }

        private void RefreshStatusPanel()
        {
            if (ChartControl == null || statusPanelHost == null)
                return;
            ChartControl.Dispatcher.InvokeAsync(RefreshStatusPanelCore);
        }

        private void RefreshStatusPanelCore()
        {
            if (statusText == null || Instrument == null)
                return;

            DateTime now = Connection.PlaybackConnection != null
                ? Connection.PlaybackConnection.Now
                : NinjaTrader.Core.Globals.Now;
            DateTime nyTime = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local, easternTimeZone);
            bool longWindowOpen = IsWithinTradingWindow(now);
            bool realtime = State == State.Realtime;

            if (!realtime)
            {
                statusText.Text = "INACTIVE";
                statusBadge.Background = MakePanelBrush(71, 85, 105);
                statusPanel.Background = MakePanelBrush(10, 16, 28);
            }
            else if (Position.MarketPosition != MarketPosition.Flat)
            {
                statusText.Text = "ACTIVE · IN POSITION";
                statusBadge.Background = MakePanelBrush(22, 101, 52);
                statusPanel.Background = longWindowOpen
                    ? MakePanelBrush(10, 31, 27)
                    : MakePanelBrush(10, 16, 28);
            }
            else if (longWindowOpen && EnableLongs)
            {
                statusText.Text = "ACTIVE · OPERATING HOURS";
                statusBadge.Background = MakePanelBrush(22, 101, 52);
                statusPanel.Background = MakePanelBrush(10, 31, 27);
            }
            else
            {
                statusText.Text = "ACTIVE · WAITING";
                statusBadge.Background = MakePanelBrush(71, 85, 105);
                statusPanel.Background = MakePanelBrush(10, 16, 28);
            }

            windowText.Text = string.Format("{0:00}:{1:00} – {2:00}:{3:00}  ·  {4}  ·  NY {5:HH:mm:ss}",
                StartTimeHHmm / 100, StartTimeHHmm % 100,
                EndTimeHHmm / 100, EndTimeHHmm % 100,
                longWindowOpen ? "OPEN" : "CLOSED", nyTime);
            windowText.Foreground = longWindowOpen
                ? MakePanelBrush(74, 222, 128)
                : Brushes.White;

            string highLevel = double.IsNaN(activeHighPrice) ? "--" : activeHighPrice.ToString("N2");
            string lowLevel = double.IsNaN(activeLowPrice) ? "--" : activeLowPrice.ToString("N2");
            levelsText.Text = string.Format("SWING HIGH  {0}     ·     LOW  {1}", highLevel, lowLevel);

            positionText.Text = Position.MarketPosition == MarketPosition.Flat
                ? string.Format("POSITION  FLAT     ·     LONG {0} / SHORT {1}",
                    EnableLongs ? "ON" : "OFF", EnableShorts ? "ON" : "OFF")
                : string.Format("POSITION  {0}     ·     AVG {1:N2}",
                    Position.MarketPosition.ToString().ToUpperInvariant(), Position.AveragePrice);

            if (!UseTrailingStop)
                trailingText.Text = "TRAILING  OFF";
            else if (TrailWithSuperTrend)
                trailingText.Text = string.Format("TRAILING  SUPERTREND {0}×ATR({1})     ·     STOP {2}",
                    SuperTrendMultiplier.ToString("0.0"), SuperTrendAtrPeriod,
                    double.IsNaN(activeSuperTrendStop) ? "--" : activeSuperTrendStop.ToString("N2"));
            else
                trailingText.Text = string.Format("TRAILING  {0} TICKS", TrailingStopTicks);

            riskText.Text = string.Format("SL  {0} ticks     ·     TP  {1}     ·     STRENGTH  {2}",
                StopLossTicks, UseProfitTarget ? TakeProfitTicks + " ticks" : "OFF", Strength);
        }

        private void ApplyPanelCorner()
        {
            if (statusPanelHost == null)
                return;

            switch (PanelCorner)
            {
                case XBFractalPanelCorner.BottomRight:
                    statusPanelHost.HorizontalAlignment = HorizontalAlignment.Right;
                    statusPanelHost.VerticalAlignment = VerticalAlignment.Bottom;
                    statusPanelHost.Margin = new Thickness(0, 16, 16, 42);
                    break;
                case XBFractalPanelCorner.TopLeft:
                    statusPanelHost.HorizontalAlignment = HorizontalAlignment.Left;
                    statusPanelHost.VerticalAlignment = VerticalAlignment.Top;
                    statusPanelHost.Margin = new Thickness(16, 42, 0, 0);
                    break;
                case XBFractalPanelCorner.TopRight:
                    statusPanelHost.HorizontalAlignment = HorizontalAlignment.Right;
                    statusPanelHost.VerticalAlignment = VerticalAlignment.Top;
                    statusPanelHost.Margin = new Thickness(0, 42, 16, 0);
                    break;
                default:
                    statusPanelHost.HorizontalAlignment = HorizontalAlignment.Left;
                    statusPanelHost.VerticalAlignment = VerticalAlignment.Bottom;
                    statusPanelHost.Margin = new Thickness(16, 16, 0, 42);
                    break;
            }
        }

        private void RemoveStatusPanel()
        {
            if (ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (panelTimer != null)
                    {
                        panelTimer.Stop();
                        panelTimer.Tick -= OnPanelTimerTick;
                        panelTimer = null;
                    }
                    if (statusPanelHost != null && UserControlCollection.Contains(statusPanelHost))
                        UserControlCollection.Remove(statusPanelHost);
                }
                catch (Exception ex)
                {
                    Print("Error removing XB Fractal panel: " + ex.Message);
                }
                finally
                {
                    statusPanelHost = null;
                    statusPanel = null;
                    statusBadge = null;
                    statusText = null;
                    windowText = null;
                    levelsText = null;
                    positionText = null;
                    trailingText = null;
                    riskText = null;
                }
            });
        }

        private static TextBlock MakePanelText(string text, double size, FontWeight weight, Brush color)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = size,
                FontWeight = weight,
                Foreground = color,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static Border MakePanelDivider()
        {
            return new Border { Height = 1, Background = MakePanelBrush(51, 65, 85), Margin = new Thickness(0, 0, 0, 12) };
        }

        private static Border MakePanelMetricCard(TextBlock text, Brush background)
        {
            return new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6),
                Child = text
            };
        }

        private static Brush MakePanelBrush(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private bool ShouldDrawLevels()
        {
            return DrawLevels && (!FastBacktestMode || State == State.Realtime);
        }

        private bool ShouldDrawTradeLines()
        {
            return DrawTradeLines && (!FastBacktestMode || State == State.Realtime);
        }
    }
}
