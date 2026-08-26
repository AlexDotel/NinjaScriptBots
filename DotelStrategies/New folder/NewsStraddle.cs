#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public enum NewsStraddlePriceReference
    {
        Last,
        BidAsk
    }

    public enum NewsStraddlePanelCorner
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight
    }

    /// <summary>
    /// Places two opposing stop entries at a scheduled time. Entries share an
    /// OCO group and every execution receives its own stop loss and profit target.
    /// </summary>
    public class NewsStraddle : Strategy
    {
        private const string LongEntryName = "NS-Long";
        private const string ShortEntryName = "NS-Short";
        private const string GhostBuyLineTag = "NS-Ghost-Buy-Level";
        private const string GhostSellLineTag = "NS-Ghost-Sell-Level";
        private const string FullVersionUrl = "https://whop.com/@isdotel/";
        private static readonly DateTime LicenseExpirationDate = new DateTime(2026, 8, 30);
        private static readonly Brush ActiveStatusBrush = MakeBrush(22, 101, 52);
        private static readonly Brush InactiveStatusBrush = MakeBrush(71, 85, 105);
        private static readonly Brush ExpiredStatusBrush = MakeBrush(153, 27, 27);
        private static readonly Brush LicenseValidBrush = MakeBrush(148, 163, 184);
        private static readonly Brush LicenseExpiredBrush = MakeBrush(251, 113, 133);
        private static readonly Brush CountdownBrush = MakeBrush(56, 189, 248);

        private Order longEntryOrder;
        private Order shortEntryOrder;
        private DateTime lastTriggeredDate;
        private DateTime submittedAt;
        private int bracketSequence;
        private bool entriesSubmitted;
        private bool licenseExpirationReported;
        private bool ghostArmed;
        private bool ghostTriggered;
        private double ghostBuyTrigger;
        private double ghostSellTrigger;

        private Grid statusPanelHost;
        private Border statusPanel;
        private Border statusBadge;
        private TextBlock statusText;
        private TextBlock scheduledTimeText;
        private TextBlock countdownText;
        private TextBlock countdownCaption;
        private TextBlock takeProfitText;
        private TextBlock stopLossText;
        private TextBlock entryText;
        private TextBlock accountText;
        private TextBlock entryModeText;
        private TextBlock licenseText;
        private Button fullVersionButton;
        private DispatcherTimer panelTimer;

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Hour", GroupName = "01. Schedule", Order = 0)]
        public int ScheduledHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Minute", GroupName = "01. Schedule", Order = 1)]
        public int ScheduledMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Second", GroupName = "01. Schedule", Order = 2)]
        public int ScheduledSecond { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Repeat daily", GroupName = "01. Schedule", Order = 3)]
        public bool RepeatDaily { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Date (when not repeating)", GroupName = "01. Schedule", Order = 4)]
        public DateTime ScheduledDate { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "Maximum trigger delay (s)", GroupName = "01. Schedule", Order = 5)]
        public int MaxTriggerDelaySeconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Entry distance (ticks)", GroupName = "02. Orders", Order = 0)]
        public int EntryDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Contracts", GroupName = "02. Orders", Order = 1)]
        public int OrderQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Price reference", GroupName = "02. Orders", Order = 2)]
        public NewsStraddlePriceReference PriceReference { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ghost entries", Description = "Keeps both entry levels internal and submits a market order only when price reaches one of them.", GroupName = "02. Orders", Order = 3)]
        public bool UseGhostEntries { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Stop loss (ticks)", GroupName = "03. Protection", Order = 0)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Take profit (ticks)", GroupName = "03. Protection", Order = 1)]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 86400)]
        [Display(Name = "Cancel entries after (s, 0=off)", GroupName = "03. Protection", Order = 2)]
        public int CancelEntriesAfterSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show chart panel", GroupName = "04. Display", Order = 0)]
        public bool ShowChartPanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Panel corner", GroupName = "04. Display", Order = 1)]
        public NewsStraddlePanelCorner PanelCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable 1-tick intrabar backtest", Description = "Allows historical processing on the internal 1-tick series. Keep disabled for normal real-time use.", GroupName = "05. Backtest", Order = 0)]
        public bool EnableIntrabarBacktest { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable debug logging", Description = "Writes detailed lifecycle, signal, order and execution events to the NinjaScript Output window.", GroupName = "06. Debug", Order = 0)]
        public bool EnableDebugLogging { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NewsStraddle";
                Description = "Scheduled news straddle with OCO stop entries and automatic protection. "
                    + "Licensed through 08/30/2026. Full version: " + FullVersionUrl;
                Calculate = Calculate.OnEachTick;
                IsUnmanaged = true;
                EntriesPerDirection = 1;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                ConnectionLossHandling = ConnectionLossHandling.Recalculate;
                IsInstantiatedOnEachOptimizationIteration = false;
                BarsRequiredToTrade = 0;
                OrderFillResolution = OrderFillResolution.Standard;

                ScheduledHour = 14;
                ScheduledMinute = 29;
                ScheduledSecond = 40;
                RepeatDaily = false;
                ScheduledDate = DateTime.Today;
                MaxTriggerDelaySeconds = 5;
                EntryDistanceTicks = 10;
                OrderQuantity = 2;
                PriceReference = NewsStraddlePriceReference.BidAsk;
                UseGhostEntries = false;
                StopLossTicks = 61;
                ProfitTargetTicks = 61;
                CancelEntriesAfterSeconds = 0;
                ShowChartPanel = true;
                PanelCorner = NewsStraddlePanelCorner.BottomLeft;
                EnableIntrabarBacktest = false;
                EnableDebugLogging = false;
            }
            else if (State == State.Configure)
            {
                // The one-tick series provides the timestamp that triggers orders,
                // independently from the chart's primary timeframe.
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                ResetRuntimeState();
                DebugLog("State.DataLoaded. Runtime state initialized.");
            }
            else if (State == State.Historical)
            {
                DebugLog("State.Historical. Intrabar backtest=" + EnableIntrabarBacktest + ".");
                CreateStatusPanel();
            }
            else if (State == State.Realtime)
            {
                // Entries are never submitted while processing historical data.
                ResetRuntimeState();
                DebugLog("State.Realtime. Historical runtime state cleared.");
                RefreshStatusPanel();
            }
            else if (State == State.Terminated)
            {
                DebugLog("State.Terminated. Removing chart resources.");
                RemoveGhostLines();
                RemoveStatusPanel();
            }
        }

        protected override void OnBarUpdate()
        {
            bool processRealtime = State == State.Realtime;
            bool processHistorical = State == State.Historical && EnableIntrabarBacktest;

            if ((!processRealtime && !processHistorical)
                || BarsInProgress != 1
                || CurrentBars[1] < 0)
                return;

            DateTime tickTime = Times[1][0];

            if (!IsLicenseValid())
            {
                if (ghostArmed || ghostBuyTrigger != 0 || ghostSellTrigger != 0)
                    RemoveGhostLines();
                ghostArmed = false;
                ghostBuyTrigger = 0;
                ghostSellTrigger = 0;
                CancelIfWorking(longEntryOrder);
                CancelIfWorking(shortEntryOrder);

                if (!licenseExpirationReported)
                {
                    Print("NewsStraddle license expired. Full version: " + FullVersionUrl);
                    licenseExpirationReported = true;
                }

                return;
            }

            if (RepeatDaily
                && entriesSubmitted
                && lastTriggeredDate != DateTime.MinValue
                && tickTime.Date > lastTriggeredDate
                && Position.MarketPosition == MarketPosition.Flat
                && !IsWorking(longEntryOrder)
                && !IsWorking(shortEntryOrder))
            {
                longEntryOrder = null;
                shortEntryOrder = null;
                submittedAt = DateTime.MinValue;
                entriesSubmitted = false;
                ghostArmed = false;
                ghostTriggered = false;
                ghostBuyTrigger = 0;
                ghostSellTrigger = 0;
                RemoveGhostLines();
                DebugLog(tickTime, "Daily runtime state reset.");
            }

            if (entriesSubmitted)
            {
                if (UseGhostEntries && ghostArmed)
                {
                    if (CancelEntriesAfterSeconds > 0
                        && tickTime >= submittedAt.AddSeconds(CancelEntriesAfterSeconds))
                    {
                        ghostArmed = false;
                        RemoveGhostLines();
                        DebugLog(tickTime, "Ghost levels expired after " + CancelEntriesAfterSeconds + " seconds.");
                        Print(string.Format("{0} NewsStraddle ghost levels expired.", tickTime));
                        return;
                    }

                    MonitorGhostLevels(tickTime);
                    return;
                }

                if (CancelEntriesAfterSeconds > 0
                    && tickTime >= submittedAt.AddSeconds(CancelEntriesAfterSeconds)
                    && Position.MarketPosition == MarketPosition.Flat)
                {
                    CancelIfWorking(longEntryOrder);
                    CancelIfWorking(shortEntryOrder);
                    DebugLog(tickTime, "Pending broker entry timeout reached; cancellation requested.");
                }

                return;
            }

            DateTime scheduledTime = new DateTime(
                tickTime.Year,
                tickTime.Month,
                tickTime.Day,
                ScheduledHour,
                ScheduledMinute,
                ScheduledSecond);

            if (!RepeatDaily && tickTime.Date != ScheduledDate.Date)
                return;

            if (lastTriggeredDate == tickTime.Date || tickTime < scheduledTime)
                return;

            double delaySeconds = (tickTime - scheduledTime).TotalSeconds;
            if (delaySeconds > MaxTriggerDelaySeconds)
            {
                // Mark the day as processed to prevent a late entry when the
                // strategy was enabled after its scheduled time.
                lastTriggeredDate = tickTime.Date;
                Print(string.Format("{0} NewsStraddle skipped: trigger was {1:F1} seconds late.", tickTime, delaySeconds));
                return;
            }

            DebugLog(tickTime, "Schedule reached. Entry mode="
                + (UseGhostEntries ? "GHOST" : "BROKER ORDERS")
                + ", historical=" + (State == State.Historical) + ".");

            if (UseGhostEntries)
                ArmGhostStraddle(tickTime);
            else
                SubmitEntryStraddle(tickTime);
        }

        private void GetReferencePrices(out double last, out double ask, out double bid)
        {
            last = Closes[1][0];

            // Historical bid/ask events are not available from a standard Last
            // tick series. Use the tick price itself so Analyzer results remain
            // deterministic; real-time behavior continues to use live Bid/Ask.
            if (State == State.Historical)
            {
                ask = last;
                bid = last;
                return;
            }

            ask = GetCurrentAsk(1);
            bid = GetCurrentBid(1);

            if (ask <= 0)
                ask = last;
            if (bid <= 0)
                bid = last;
        }

        private void ArmGhostStraddle(DateTime tickTime)
        {
            double last;
            double ask;
            double bid;
            GetReferencePrices(out last, out ask, out bid);

            double buyReference = PriceReference == NewsStraddlePriceReference.BidAsk ? ask : last;
            double sellReference = PriceReference == NewsStraddlePriceReference.BidAsk ? bid : last;

            ghostBuyTrigger = RoundToTick(buyReference + EntryDistanceTicks * TickSize);
            ghostSellTrigger = RoundToTick(sellReference - EntryDistanceTicks * TickSize);
            ghostArmed = true;
            ghostTriggered = false;
            entriesSubmitted = true;
            submittedAt = tickTime;
            lastTriggeredDate = tickTime.Date;

            DrawGhostLines();

            DebugLog(tickTime, string.Format(
                "Ghost armed. Last={0}, Bid={1}, Ask={2}, BuyTrigger={3}, SellTrigger={4}, Quantity={5}.",
                last, bid, ask, ghostBuyTrigger, ghostSellTrigger, OrderQuantity));

            Print(string.Format(
                "{0} NewsStraddle ghost levels armed: BUY {1} / SELL {2}, quantity {3}.",
                tickTime, ghostBuyTrigger, ghostSellTrigger, OrderQuantity));
        }

        private void MonitorGhostLevels(DateTime tickTime)
        {
            double last;
            double ask;
            double bid;
            GetReferencePrices(out last, out ask, out bid);

            double buyMonitorPrice = PriceReference == NewsStraddlePriceReference.BidAsk ? ask : last;
            double sellMonitorPrice = PriceReference == NewsStraddlePriceReference.BidAsk ? bid : last;

            if (buyMonitorPrice >= ghostBuyTrigger)
            {
                ghostArmed = false;
                ghostTriggered = true;
                RemoveGhostLines();
                SubmitOrderUnmanaged(1, OrderAction.Buy, OrderType.Market,
                    OrderQuantity, 0, 0, string.Empty, LongEntryName);
                DebugLog(tickTime, string.Format(
                    "Ghost BUY triggered. MonitorPrice={0}, Trigger={1}.", buyMonitorPrice, ghostBuyTrigger));
                Print(string.Format("{0} NewsStraddle ghost BUY triggered at market.", tickTime));
            }
            else if (sellMonitorPrice <= ghostSellTrigger)
            {
                ghostArmed = false;
                ghostTriggered = true;
                RemoveGhostLines();
                SubmitOrderUnmanaged(1, OrderAction.SellShort, OrderType.Market,
                    OrderQuantity, 0, 0, string.Empty, ShortEntryName);
                DebugLog(tickTime, string.Format(
                    "Ghost SELL triggered. MonitorPrice={0}, Trigger={1}.", sellMonitorPrice, ghostSellTrigger));
                Print(string.Format("{0} NewsStraddle ghost SELL triggered at market.", tickTime));
            }
        }

        private void DrawGhostLines()
        {
            if (ChartControl == null || !ghostArmed)
                return;

            Draw.HorizontalLine(this, GhostBuyLineTag, ghostBuyTrigger, Brushes.LimeGreen);
            Draw.HorizontalLine(this, GhostSellLineTag, ghostSellTrigger, Brushes.OrangeRed);
        }

        private void RemoveGhostLines()
        {
            RemoveDrawObject(GhostBuyLineTag);
            RemoveDrawObject(GhostSellLineTag);
        }

        private void SubmitEntryStraddle(DateTime tickTime)
        {
            double last;
            double ask;
            double bid;
            GetReferencePrices(out last, out ask, out bid);

            double buyReference = PriceReference == NewsStraddlePriceReference.BidAsk ? ask : last;
            double sellReference = PriceReference == NewsStraddlePriceReference.BidAsk ? bid : last;
            double buyStop = RoundToTick(Math.Max(ask + TickSize, buyReference + EntryDistanceTicks * TickSize));
            double sellStop = RoundToTick(Math.Min(bid - TickSize, sellReference - EntryDistanceTicks * TickSize));
            string entryOco = "NS-E-" + tickTime.ToString("yyyyMMddHHmmssfff");

            entriesSubmitted = true;
            submittedAt = tickTime;
            lastTriggeredDate = tickTime.Date;

            SubmitOrderUnmanaged(1, OrderAction.Buy, OrderType.StopMarket,
                OrderQuantity, 0, buyStop, entryOco, LongEntryName);
            SubmitOrderUnmanaged(1, OrderAction.SellShort, OrderType.StopMarket,
                OrderQuantity, 0, sellStop, entryOco, ShortEntryName);

            DebugLog(tickTime, string.Format(
                "Broker entries submitted. Last={0}, Bid={1}, Ask={2}, BuyStop={3}, SellStop={4}, OCO={5}, Quantity={6}.",
                last, bid, ask, buyStop, sellStop, entryOco, OrderQuantity));

            Print(string.Format(
                "{0} NewsStraddle submitted: BUY STOP {1} / SELL STOP {2}, quantity {3}.",
                tickTime, buyStop, sellStop, OrderQuantity));
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string nativeError)
        {
            if (order.Name == LongEntryName)
                longEntryOrder = order;
            else if (order.Name == ShortEntryName)
                shortEntryOrder = order;

            DebugLog(time, string.Format(
                "Order update. Name={0}, State={1}, Type={2}, Action={3}, Quantity={4}, Filled={5}, Limit={6}, Stop={7}, AvgFill={8}, Error={9}, Comment={10}.",
                order.Name, orderState, order.OrderType, order.OrderAction, quantity, filled,
                limitPrice, stopPrice, averageFillPrice, error, nativeError));

            if (error != ErrorCode.NoError || orderState == OrderState.Rejected)
            {
                Print(string.Format("{0} ERROR {1}: {2} - {3}", time, order.Name, error, nativeError));

                if (order.Name == LongEntryName)
                    CancelIfWorking(shortEntryOrder);
                else if (order.Name == ShortEntryName)
                    CancelIfWorking(longEntryOrder);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null || quantity <= 0)
                return;

            DebugLog(time, string.Format(
                "Execution. Name={0}, ExecutionId={1}, OrderId={2}, Price={3}, Quantity={4}, MarketPosition={5}.",
                execution.Order.Name, executionId, orderId, price, quantity, marketPosition));

            if (execution.Order.Name == LongEntryName)
            {
                CancelIfWorking(shortEntryOrder);
                SubmitLongBracket(price, quantity, time);
            }
            else if (execution.Order.Name == ShortEntryName)
            {
                CancelIfWorking(longEntryOrder);
                SubmitShortBracket(price, quantity, time);
            }
        }

        private void SubmitLongBracket(double fillPrice, int quantity, DateTime time)
        {
            int sequence = ++bracketSequence;
            string oco = "NS-LX-" + time.ToString("HHmmssfff") + "-" + sequence;
            double stop = RoundToTick(fillPrice - StopLossTicks * TickSize);
            double target = RoundToTick(fillPrice + ProfitTargetTicks * TickSize);

            SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.StopMarket,
                quantity, 0, stop, oco, "NS-LS-" + sequence);
            SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Limit,
                quantity, target, 0, oco, "NS-LT-" + sequence);

            DebugLog(time, string.Format(
                "Long bracket submitted. Fill={0}, Stop={1}, Target={2}, Quantity={3}, OCO={4}.",
                fillPrice, stop, target, quantity, oco));
        }

        private void SubmitShortBracket(double fillPrice, int quantity, DateTime time)
        {
            int sequence = ++bracketSequence;
            string oco = "NS-SX-" + time.ToString("HHmmssfff") + "-" + sequence;
            double stop = RoundToTick(fillPrice + StopLossTicks * TickSize);
            double target = RoundToTick(fillPrice - ProfitTargetTicks * TickSize);

            SubmitOrderUnmanaged(1, OrderAction.BuyToCover, OrderType.StopMarket,
                quantity, 0, stop, oco, "NS-SS-" + sequence);
            SubmitOrderUnmanaged(1, OrderAction.BuyToCover, OrderType.Limit,
                quantity, target, 0, oco, "NS-ST-" + sequence);

            DebugLog(time, string.Format(
                "Short bracket submitted. Fill={0}, Stop={1}, Target={2}, Quantity={3}, OCO={4}.",
                fillPrice, stop, target, quantity, oco));
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

                    StackPanel content = new StackPanel
                    {
                        Orientation = Orientation.Vertical
                    };

                    StackPanel header = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 13)
                    };

                    statusText = MakeText("INACTIVE", 12, FontWeights.Bold, Brushes.White);
                    statusBadge = new Border
                    {
                        Background = MakeBrush(71, 85, 105),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(10, 4, 10, 4),
                        Child = statusText
                    };
                    header.Children.Add(statusBadge);

                    TextBlock brand = MakeText("NEWSSTRADDLE", 13, FontWeights.SemiBold,
                        MakeBrush(148, 163, 184));
                    brand.Margin = new Thickness(10, 4, 0, 0);
                    header.Children.Add(brand);
                    content.Children.Add(header);

                    TextBlock timeCaption = MakeText("SCHEDULED TIME", 10, FontWeights.SemiBold,
                        MakeBrush(100, 116, 139));
                    content.Children.Add(timeCaption);

                    scheduledTimeText = MakeText("--:--:--", 22, FontWeights.Bold, Brushes.White);
                    scheduledTimeText.Margin = new Thickness(0, 2, 0, 12);
                    content.Children.Add(scheduledTimeText);

                    content.Children.Add(MakeDivider());

                    countdownCaption = MakeText("COUNTDOWN", 10, FontWeights.SemiBold,
                        MakeBrush(100, 116, 139));
                    countdownCaption.Margin = new Thickness(0, 12, 0, 0);
                    content.Children.Add(countdownCaption);

                    countdownText = MakeText("-- s", 28, FontWeights.Bold, MakeBrush(56, 189, 248));
                    countdownText.FontFamily = new FontFamily("Consolas");
                    countdownText.Margin = new Thickness(0, 0, 0, 13);
                    content.Children.Add(countdownText);

                    accountText = MakeText("", 12, FontWeights.SemiBold, Brushes.White);
                    entryModeText = MakeText("", 12, FontWeights.SemiBold, MakeBrush(125, 211, 252));
                    takeProfitText = MakeText("", 13, FontWeights.SemiBold, MakeBrush(74, 222, 128));
                    stopLossText = MakeText("", 13, FontWeights.SemiBold, MakeBrush(251, 113, 133));
                    entryText = MakeText("", 12, FontWeights.Normal, MakeBrush(203, 213, 225));
                    content.Children.Add(MakeMetricCard(accountText, MakeBrush(30, 41, 59)));
                    content.Children.Add(MakeMetricCard(entryModeText, MakeBrush(12, 74, 110)));
                    content.Children.Add(MakeMetricCard(takeProfitText, MakeBrush(20, 83, 45)));
                    content.Children.Add(MakeMetricCard(stopLossText, MakeBrush(127, 29, 29)));
                    content.Children.Add(MakeMetricCard(entryText, MakeBrush(30, 41, 59)));

                    content.Children.Add(MakeDivider());
                    licenseText = MakeText("", 11, FontWeights.SemiBold, MakeBrush(148, 163, 184));
                    licenseText.Margin = new Thickness(0, 11, 0, 2);
                    content.Children.Add(licenseText);

                    fullVersionButton = new Button
                    {
                        Content = "GET FULL VERSION",
                        ToolTip = FullVersionUrl,
                        Background = MakeBrush(2, 132, 199),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(12, 8, 12, 8),
                        Margin = new Thickness(0, 8, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Cursor = Cursors.Hand,
                        Template = CreateRoundedButtonTemplate()
                    };
                    fullVersionButton.Click += OnFullVersionButtonClick;
                    content.Children.Add(fullVersionButton);

                    statusPanel = new Border
                    {
                        Background = MakeBrush(10, 16, 28),
                        BorderBrush = MakeBrush(51, 65, 85),
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
                        Name = "NewsStraddleStatusPanel",
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(16, 16, 0, 42),
                        IsHitTestVisible = true
                    };
                    statusPanelHost.Children.Add(statusPanel);
                    System.Windows.Controls.Panel.SetZIndex(statusPanelHost, 99999);
                    ApplyPanelCorner();
                    UserControlCollection.Add(statusPanelHost);

                    panelTimer = new DispatcherTimer(DispatcherPriority.Background,
                        ChartControl.Dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(250)
                    };
                    panelTimer.Tick += OnPanelTimerTick;
                    panelTimer.Start();
                    RefreshStatusPanelCore();
                }
                catch (Exception ex)
                {
                    Print("Error creating NewsStraddle panel: " + ex.Message);
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
            DateTime target = RepeatDaily
                ? new DateTime(now.Year, now.Month, now.Day, ScheduledHour, ScheduledMinute, ScheduledSecond)
                : ScheduledDate.Date.AddHours(ScheduledHour).AddMinutes(ScheduledMinute).AddSeconds(ScheduledSecond);
            bool schedulePassed = !RepeatDaily && target < now && !entriesSubmitted;
            bool active = State == State.Realtime;
            bool licenseValid = IsLicenseValid();
            bool entryWorking = IsWorking(longEntryOrder) || IsWorking(shortEntryOrder);

            string operationalState;
            if (!licenseValid)
                operationalState = "LICENSE EXPIRED";
            else if (schedulePassed)
                operationalState = active ? "ACTIVE · DATE PASSED" : "INACTIVE · DATE PASSED";
            else if (!active)
                operationalState = "INACTIVE";
            else if (Position.MarketPosition != MarketPosition.Flat)
                operationalState = "ACTIVE · IN POSITION";
            else if (ghostArmed)
                operationalState = "ACTIVE · GHOST ARMED";
            else if (entryWorking)
                operationalState = "ACTIVE · ORDERS WORKING";
            else if (entriesSubmitted)
                operationalState = "ACTIVE · COMPLETED";
            else
                operationalState = "ACTIVE · WAITING";

            statusText.Text = operationalState;
            statusBadge.Background = !licenseValid || schedulePassed
                ? ExpiredStatusBrush
                : active ? ActiveStatusBrush : InactiveStatusBrush;

            scheduledTimeText.Text = string.Format("{0:00}:{1:00}:{2:00}  {3}",
                ScheduledHour, ScheduledMinute, ScheduledSecond,
                RepeatDaily ? "· DAILY" : "· " + ScheduledDate.ToString("MM/dd/yyyy"));

            if (RepeatDaily && target < now && lastTriggeredDate == now.Date)
                target = target.AddDays(1);

            double secondsRemaining = Math.Max(0, Math.Ceiling((target - now).TotalSeconds));
            if (!licenseValid)
            {
                countdownCaption.Text = "LICENSE STATUS";
                countdownText.Text = "EXPIRED";
                countdownText.FontSize = 22;
                countdownText.Foreground = LicenseExpiredBrush;
            }
            else if (entriesSubmitted && lastTriggeredDate == now.Date)
            {
                countdownCaption.Text = ghostArmed ? "GHOST LEVELS" : "TODAY'S EXECUTION";
                countdownText.Text = ghostArmed
                    ? "WAITING FOR TRIGGER"
                    : entryWorking ? "ORDERS SUBMITTED" : "COMPLETED";
                countdownText.FontSize = 18;
                countdownText.Foreground = CountdownBrush;
            }
            else if (schedulePassed)
            {
                countdownCaption.Text = "SCHEDULE STATUS";
                countdownText.Text = "DATE PASSED";
                countdownText.FontSize = 22;
                countdownText.Foreground = LicenseExpiredBrush;
            }
            else
            {
                countdownCaption.Text = "COUNTDOWN";
                countdownText.Text = FormatCountdown(TimeSpan.FromSeconds(secondsRemaining));
                countdownText.FontSize = 18;
                countdownText.Foreground = CountdownBrush;
            }

            double tickValue = TickSize * Instrument.MasterInstrument.PointValue;
            double targetDollars = ProfitTargetTicks * tickValue * OrderQuantity;
            double stopDollars = StopLossTicks * tickValue * OrderQuantity;
            accountText.Text = "ACCOUNT   " + (Account == null ? "NOT SELECTED" : Account.Name);
            entryModeText.Text = (UseGhostEntries
                ? "ENTRY MODE   GHOST" + (ghostArmed ? " · ARMED" : ghostTriggered ? " · TRIGGERED" : "")
                : "ENTRY MODE   BROKER ORDERS")
                + (EnableDebugLogging ? " · DEBUG ON" : "");
            takeProfitText.Text = string.Format("TP   {0:N0} ticks   ·   ${1:N2}",
                ProfitTargetTicks, targetDollars);
            stopLossText.Text = string.Format("SL   {0:N0} ticks   ·   ${1:N2}",
                StopLossTicks, stopDollars);
            entryText.Text = string.Format("Entry ±{0:N0} ticks   ·   {1} contract{2}",
                EntryDistanceTicks, OrderQuantity, OrderQuantity == 1 ? "" : "s");
            licenseText.Text = licenseValid
                ? "LICENSE VALID THROUGH 08/30/2026"
                : "LICENSE EXPIRED ON 08/30/2026";
            licenseText.Foreground = licenseValid
                ? LicenseValidBrush
                : LicenseExpiredBrush;
        }

        private void ApplyPanelCorner()
        {
            if (statusPanelHost == null)
                return;

            switch (PanelCorner)
            {
                case NewsStraddlePanelCorner.BottomRight:
                    statusPanelHost.HorizontalAlignment = HorizontalAlignment.Right;
                    statusPanelHost.VerticalAlignment = VerticalAlignment.Bottom;
                    statusPanelHost.Margin = new Thickness(0, 16, 16, 42);
                    break;
                case NewsStraddlePanelCorner.TopLeft:
                    statusPanelHost.HorizontalAlignment = HorizontalAlignment.Left;
                    statusPanelHost.VerticalAlignment = VerticalAlignment.Top;
                    statusPanelHost.Margin = new Thickness(16, 42, 0, 0);
                    break;
                case NewsStraddlePanelCorner.TopRight:
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

        private static string FormatCountdown(TimeSpan remaining)
        {
            int totalDays = Math.Max(0, (int)Math.Floor(remaining.TotalDays));
            int weeks = totalDays / 7;
            int days = totalDays % 7;

            return string.Format("{0}w · {1}d · {2:00}h · {3:00}m · {4:00}s",
                weeks, days, remaining.Hours, remaining.Minutes, remaining.Seconds);
        }

        private void OnFullVersionButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(FullVersionUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Print("Unable to open full-version page: " + ex.Message);
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

                    if (fullVersionButton != null)
                        fullVersionButton.Click -= OnFullVersionButtonClick;

                    if (statusPanelHost != null && UserControlCollection.Contains(statusPanelHost))
                        UserControlCollection.Remove(statusPanelHost);
                }
                catch (Exception ex)
                {
                    Print("Error removing NewsStraddle panel: " + ex.Message);
                }
                finally
                {
                    statusPanelHost = null;
                    statusPanel = null;
                    statusBadge = null;
                    statusText = null;
                    scheduledTimeText = null;
                    countdownText = null;
                    countdownCaption = null;
                    takeProfitText = null;
                    stopLossText = null;
                    entryText = null;
                    accountText = null;
                    entryModeText = null;
                    licenseText = null;
                    fullVersionButton = null;
                }
            });
        }

        private static TextBlock MakeText(string text, double size, FontWeight weight, Brush color)
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

        private static Border MakeDivider()
        {
            return new Border
            {
                Height = 1,
                Background = MakeBrush(51, 65, 85)
            };
        }

        private static Border MakeMetricCard(TextBlock text, Brush background)
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

        private static ControlTemplate CreateRoundedButtonTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border), "ButtonBorder");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(Button))
            {
                VisualTree = border
            };

            Trigger hoverTrigger = new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                MakeBrush(3, 155, 229), "ButtonBorder"));
            template.Triggers.Add(hoverTrigger);

            Trigger pressedTrigger = new Trigger
            {
                Property = Button.IsPressedProperty,
                Value = true
            };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                MakeBrush(3, 105, 161), "ButtonBorder"));
            template.Triggers.Add(pressedTrigger);

            return template;
        }

        private static Brush MakeBrush(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static bool IsLicenseValid()
        {
            return NinjaTrader.Core.Globals.Now.Date <= LicenseExpirationDate;
        }

        private void DebugLog(string message)
        {
            DebugLog(NinjaTrader.Core.Globals.Now, message);
        }

        private void DebugLog(DateTime timestamp, string message)
        {
            if (!EnableDebugLogging)
                return;

            Print(string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [NewsStraddle DEBUG] [{1}] {2}",
                timestamp, State, message));
        }

        private double RoundToTick(double price)
        {
            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private static bool IsWorking(Order order)
        {
            return order != null
                && (order.OrderState == OrderState.Accepted
                    || order.OrderState == OrderState.Submitted
                    || order.OrderState == OrderState.Working
                    || order.OrderState == OrderState.PartFilled
                    || order.OrderState == OrderState.ChangePending
                    || order.OrderState == OrderState.ChangeSubmitted);
        }

        private void CancelIfWorking(Order order)
        {
            if (IsWorking(order))
            {
                DebugLog("Cancel requested. Name=" + order.Name
                    + ", State=" + order.OrderState
                    + ", Quantity=" + order.Quantity
                    + ", Filled=" + order.Filled + ".");
                CancelOrder(order);
            }
        }

        private void ResetRuntimeState()
        {
            longEntryOrder = null;
            shortEntryOrder = null;
            lastTriggeredDate = DateTime.MinValue;
            submittedAt = DateTime.MinValue;
            bracketSequence = 0;
            entriesSubmitted = false;
            licenseExpirationReported = false;
            ghostArmed = false;
            ghostTriggered = false;
            ghostBuyTrigger = 0;
            ghostSellTrigger = 0;
        }
    }
}
