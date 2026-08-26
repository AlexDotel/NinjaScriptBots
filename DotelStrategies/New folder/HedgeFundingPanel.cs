#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public enum HedgeEntryMode { Market, StopLimit, Limit }
    public enum HedgeFundingPreset { Custom, BulenoxQualification25KTrailing }

    /// <summary>
    /// Populates the NinjaScript property grid with the accounts currently
    /// available to NinjaTrader, instead of requiring the name to be typed.
    /// </summary>
    public class HedgeAccountNameConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return false; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            string[] names = Account.All
                .Where(a => a != null && a.Connection != null && a.Connection.Status == ConnectionStatus.Connected)
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
            return new StandardValuesCollection(names);
        }

        private static bool AvailableMargin(Account account)
        {
            try
            {
                double margin = account.Get(AccountItem.ExcessIntradayMargin, Currency.UsDollar);
                return !double.IsNaN(margin) && !double.IsInfinity(margin) && margin > 0;
            }
            catch { return true; }
        }
    }

    /// <summary>
    /// Manual two-account hedge panel. Account A receives the selected side and
    /// Account B receives the opposite side. SL/TP are symmetric per account.
    /// </summary>
    public class HedgeFundingPanel : Strategy
    {
        private Grid host, body;
        private Chart chartWindow;
        private ChartTrader chartTrader;
        private Grid chartTraderGrid;
        private ComboBox accountABox, accountBBox, modeBox, presetBox;
        private TextBox entryBox, qtyBox, slBox, tpBox;
        private TextBlock accountText, quoteText, riskText, presetText, statusText;
        private Button buyButton, sellButton, cancelButton, closeButton;
        private DispatcherTimer timer;
        private readonly string[] accountNames = new string[2];
        private readonly HashSet<Account> monitoredAccounts = new HashSet<Account>();

        private static IEnumerable<Account> TradableAccounts()
        {
            // Match the Accounts tab exactly: every account registered in
            // NinjaTrader is available in the selector, with no extra margin
            // or connection filtering.
            return Account.All.Where(a => IsActiveAccount(a));
        }

        private static bool IsActiveAccount(Account account)
        {
            return account != null && account.Connection != null
                && account.Connection.Status == ConnectionStatus.Connected;
        }

        private static bool HasAvailableMargin(Account account)
        {
            try
            {
                double margin = account.Get(AccountItem.ExcessIntradayMargin, Currency.UsDollar);
                if (double.IsNaN(margin) || double.IsInfinity(margin))
                    return false;
                return margin > 0;
            }
            catch
            {
                // Some adapters do not publish intraday margin. Keep connected
                // accounts visible in that case rather than hiding valid ones.
                return true;
            }
        }

        [NinjaScriptProperty, TypeConverter(typeof(HedgeAccountNameConverter)), Display(Name="Account A", GroupName="01. Accounts", Order=0)]
        public string AccountA { get; set; }
        [NinjaScriptProperty, TypeConverter(typeof(HedgeAccountNameConverter)), Display(Name="Account B", GroupName="01. Accounts", Order=1)]
        public string AccountB { get; set; }
        [NinjaScriptProperty, Range(1, 1000), Display(Name="Quantity", GroupName="02. Orders", Order=0)]
        public int Quantity { get; set; }
        [NinjaScriptProperty, Range(1, 100000), Display(Name="Stop loss (ticks)", GroupName="03. Protection", Order=0)]
        public int StopLossTicks { get; set; }
        [NinjaScriptProperty, Range(1, 100000), Display(Name="Profit target (ticks)", GroupName="03. Protection", Order=1)]
        public int ProfitTargetTicks { get; set; }
        [NinjaScriptProperty, Range(0, 100000), Display(Name="Entry price (0 = live)", GroupName="02. Orders", Order=1)]
        public double EntryPrice { get; set; }
        [NinjaScriptProperty, Display(Name="Entry mode", GroupName="02. Orders", Order=2)]
        public HedgeEntryMode EntryMode { get; set; }
        [NinjaScriptProperty, Display(Name="Risk preset", GroupName="04. Bulenox preset", Order=0)]
        public HedgeFundingPreset RiskPreset { get; set; }
        [NinjaScriptProperty, Range(1, 3), Display(Name="Max contracts", GroupName="04. Bulenox preset", Order=1)]
        public int MaxContractsAllowed { get; set; }
        [NinjaScriptProperty, Range(1, 100000), Display(Name="Profit objective ($)", GroupName="04. Bulenox preset", Order=2)]
        public double ProfitObjectiveDollars { get; set; }
        [NinjaScriptProperty, Range(1, 100000), Display(Name="Trailing DDW ($)", GroupName="04. Bulenox preset", Order=3)]
        public double TrailingDrawdownDollars { get; set; }
        [NinjaScriptProperty, Range(1, 1000), Display(Name="Trades to target", GroupName="04. Bulenox preset", Order=4)]
        public int TradesToTarget { get; set; }
        [NinjaScriptProperty, Range(0, 1000), Display(Name="Commission / contract round trip ($)", GroupName="04. Bulenox preset", Order=5)]
        public double CommissionPerContractRoundTrip { get; set; }
        [NinjaScriptProperty, Display(Name="Debug logging", GroupName="05. Diagnostics", Order=0)]
        public bool EnableDebugLogging { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "HedgeFundingPanel"; Calculate = Calculate.OnEachTick;
                IsUnmanaged = true; AccountA = ""; AccountB = ""; Quantity = 1;
                StopLossTicks = 20; ProfitTargetTicks = 20; EntryPrice = 0;
                EntryMode = HedgeEntryMode.Market;
                RiskPreset = HedgeFundingPreset.BulenoxQualification25KTrailing;
                MaxContractsAllowed = 3; ProfitObjectiveDollars = 1500; TrailingDrawdownDollars = 1500;
                TradesToTarget = 1; CommissionPerContractRoundTrip = 4.50;
                EnableDebugLogging = true;
                IsOverlay = true; RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
            }
            else if (State == State.Realtime) CreatePanel();
            else if (State == State.Terminated) RemovePanel();
        }

        private void CreatePanel()
        {
            if (ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (host != null) return;
                chartWindow = Window.GetWindow(ChartControl.Parent) as Chart;
                if (chartWindow == null) return;
                chartTrader = chartWindow.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                if (chartTrader == null) return;
                chartTraderGrid = chartTrader.FindName("grdMain") as Grid;
                if (chartTraderGrid == null) return;
                body = new Grid(); body.RowDefinitions.Add(new RowDefinition());
                StackPanel p = new StackPanel();
                p.Children.Add(Text("HEDGE FUNDER", 15, FontWeights.Bold, Brushes.White));
                p.Children.Add(Text("SYMMETRIC DRAWDOWN CONTROL", 10, FontWeights.SemiBold, Brush(125,211,252)));
                presetBox = Combo(new[]{"CUSTOM", "BULENOX QUALIFICATION 25K TRAILING"});
                presetBox.SelectedIndex = RiskPreset == HedgeFundingPreset.BulenoxQualification25KTrailing ? 1 : 0;
                presetBox.SelectionChanged += (s,e) => ApplyPanelPreset();
                p.Children.Add(Labelled("RISK PRESET", presetBox));
                presetText = Text("", 10, FontWeights.SemiBold, Brush(251,191,36));
                p.Children.Add(Card(presetText, Brush(66,48,8)));
                accountABox = Accounts("ACCOUNT A"); p.Children.Add(accountABox);
                accountBBox = Accounts("ACCOUNT B");
                if (accountBBox.Items.Count > 1)
                    accountBBox.SelectedIndex = 1;
                p.Children.Add(accountBBox);
                AttachAccountEvents();
                if (RiskPreset == HedgeFundingPreset.BulenoxQualification25KTrailing)
                {
                    Quantity = EffectiveMaxContracts();
                    EntryMode = HedgeEntryMode.Market;
                    EntryPrice = 0;
                    TradesToTarget = 1;
                    StopLossTicks = CalculateTargetTicks(EffectiveMaxContracts());
                    ProfitTargetTicks = StopLossTicks;
                }
                modeBox = Combo(new[]{"MARKET", "LIMIT"}); modeBox.SelectedIndex = EntryMode == HedgeEntryMode.Market ? 0 : 1; p.Children.Add(Labelled("ENTRY MODE", modeBox));
                entryBox = Input(EntryPrice.ToString("0.########")); p.Children.Add(Labelled("PRICE (STOP/LIMIT)", entryBox));
                qtyBox = Input((RiskPreset == HedgeFundingPreset.BulenoxQualification25KTrailing ? EffectiveMaxContracts() : Quantity).ToString());
                p.Children.Add(Labelled("CONTRACTS", qtyBox));
                slBox = Input(StopLossTicks.ToString()); p.Children.Add(Labelled("SL TICKS", slBox));
                tpBox = Input(ProfitTargetTicks.ToString()); p.Children.Add(Labelled("TP TICKS", tpBox));
                accountText = Text("", 11, FontWeights.SemiBold, Brushes.White); p.Children.Add(Card(accountText, Brush(30,41,59)));
                quoteText = Text("", 11, FontWeights.SemiBold, Brush(125,211,252)); p.Children.Add(Card(quoteText, Brush(12,74,110)));
                riskText = Text("", 11, FontWeights.SemiBold, Brush(74,222,128)); p.Children.Add(Card(riskText, Brush(20,83,45)));
                StackPanel buttons = new StackPanel { Orientation=Orientation.Horizontal };
                buyButton = Button("BUY A ⇄ SELL B", Brush(22,101,52)); sellButton = Button("SELL A ⇄ BUY B", Brush(127,29,29)); cancelButton = Button("CANCEL", Brush(71,85,105)); closeButton = Button("CLOSE", Brush(153,27,27));
                buyButton.Click += (s,e)=>Submit(false); sellButton.Click += (s,e)=>Submit(true); cancelButton.Click += (s,e)=>CancelOrders(); closeButton.Click += (s,e)=>ClosePositions();
                buttons.Children.Add(buyButton); buttons.Children.Add(sellButton); buttons.Children.Add(cancelButton); buttons.Children.Add(closeButton); p.Children.Add(buttons);
                statusText = Text("READY", 10, FontWeights.SemiBold, Brush(148,163,184)); p.Children.Add(statusText);
                body.Children.Add(p);
                host = new Grid { Name="HedgeFundingPanel", HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Bottom, Margin=new Thickness(6), IsHitTestVisible=true, Focusable=true };
                host.Children.Add(new Border { Background=Brush(10,16,28), BorderBrush=Brush(51,65,85), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Padding=new Thickness(14), HorizontalAlignment=HorizontalAlignment.Stretch, Child=body });
                int targetRow = chartTraderGrid.RowDefinitions.Count;
                chartTraderGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
                Grid.SetRow(host, targetRow); Grid.SetColumn(host, 0);
                System.Windows.Controls.Panel.SetZIndex(host,99999); chartTraderGrid.Children.Add(host);
                timer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) }; timer.Tick += (s,e)=>Refresh(); timer.Start(); Refresh();
            });
        }

        private void ApplyPanelPreset()
        {
            if (presetBox == null || presetBox.SelectedIndex < 0) return;
            if (presetBox.SelectedIndex == 1)
            {
                RiskPreset = HedgeFundingPreset.BulenoxQualification25KTrailing;
                MaxContractsAllowed = 3;
                ProfitObjectiveDollars = 1500;
                TrailingDrawdownDollars = 1500;
                if (qtyBox != null) qtyBox.Text = EffectiveMaxContracts().ToString();
                int calculatedTicks = CalculateTargetTicks(EffectiveMaxContracts());
                StopLossTicks = calculatedTicks;
                ProfitTargetTicks = calculatedTicks;
                if (slBox != null) slBox.Text = calculatedTicks.ToString();
                if (tpBox != null) tpBox.Text = calculatedTicks.ToString();
            }
            else
                RiskPreset = HedgeFundingPreset.Custom;
            Refresh();
        }

        private void AttachAccountEvents()
        {
            foreach (Account account in monitoredAccounts)
                account.ExecutionUpdate -= OnAccountExecutionUpdate;
            monitoredAccounts.Clear();
            foreach (Account account in new[] { Selected(accountABox), Selected(accountBBox) }.Where(x => x != null).Distinct())
            {
                account.ExecutionUpdate += OnAccountExecutionUpdate;
                monitoredAccounts.Add(account);
            }
        }

        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                Execution execution = e.Execution;
                if (execution == null || execution.Order == null || execution.Order.Name != "HFP-Entry") return;
                int quantity = execution.Quantity;
                double fillPrice = Round(execution.Price);
                int slTicks = Int(slBox, StopLossTicks);
                int tpTicks = Int(tpBox, ProfitTargetTicks);
                bool longPosition = execution.Order.OrderAction == OrderAction.Buy;
                double stop = Round(longPosition ? fillPrice - slTicks * TickSize : fillPrice + slTicks * TickSize);
                double target = Round(longPosition ? fillPrice + tpTicks * TickSize : fillPrice - tpTicks * TickSize);
                OrderAction exitAction = longPosition ? OrderAction.Sell : OrderAction.BuyToCover;
                string oco = "HFP-BRACKET-" + Guid.NewGuid().ToString("N");
                Account account = sender as Account;
                if (account == null) return;
                Order sl = account.CreateOrder(Instrument, exitAction, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Day, quantity, 0, stop, oco, "HFP-SL", DateTime.MaxValue, null);
                Order tp = account.CreateOrder(Instrument, exitAction, OrderType.Limit, OrderEntry.Manual, TimeInForce.Day, quantity, target, 0, oco, "HFP-TP", DateTime.MaxValue, null);
                account.Submit(new[] { sl, tp });
                DebugLog("Bracket submitted | Account={0} Entry={1} Qty={2} Fill={3} SL={4} TP={5} OCO={6}", account.Name, execution.Order.OrderAction, quantity, fillPrice, stop, target, oco);
            }
            catch (Exception ex) { DebugLog("Bracket ERROR | {0}\n{1}", ex.Message, ex.StackTrace); }
        }

        private int CalculateTargetTicks(int contracts)
        {
            if (Instrument == null || TickSize <= 0 || contracts <= 0) return 1;
            double tickValue = TickSize * Instrument.MasterInstrument.PointValue;
            double netDollarsPerTrade = ProfitObjectiveDollars / Math.Max(1, TradesToTarget);
            double grossNeeded = netDollarsPerTrade + GetCommissionPerContractRoundTrip() * contracts;
            int ticks = Math.Max(1, (int)Math.Ceiling(grossNeeded / (tickValue * contracts)));
            DebugLog("Ticks | Instrument={0} Qty={1} TickValue={2} Target={3} CommissionRT={4} Ticks={5}", Instrument.MasterInstrument.Name, contracts, tickValue, ProfitObjectiveDollars, GetCommissionPerContractRoundTrip(), ticks);
            return ticks;
        }

        private int EffectiveMaxContracts()
        {
            if (RiskPreset != HedgeFundingPreset.BulenoxQualification25KTrailing)
                return MaxContractsAllowed;
            string name = Instrument == null || Instrument.MasterInstrument == null ? string.Empty : Instrument.MasterInstrument.Name.ToUpperInvariant();
            bool micro = name.StartsWith("M");
            return MaxContractsAllowed * (micro ? 10 : 1);
        }

        private double GetCommissionPerContractRoundTrip()
        {
            if (RiskPreset != HedgeFundingPreset.BulenoxQualification25KTrailing)
                return CommissionPerContractRoundTrip;
            string symbol = Instrument == null || Instrument.MasterInstrument == null
                ? string.Empty : Instrument.MasterInstrument.Name.ToUpperInvariant();
            switch (symbol)
            {
                case "MNQ": case "MES": case "M2K": case "MYM": return 1.22;
                case "NQ": case "ES": case "RTY": case "EMD": case "YM": return 4.18;
                case "MCL": return 1.52;
                case "CL": return 4.52;
                case "MGC": return 1.52;
                case "GC": return 4.62;
                case "MBT": return 5.52;
                case "MET": return 0.92;
                case "M6A": case "M6B": case "M6E": return 1.00;
                case "6A": case "6B": case "6C": case "6E": case "6J": case "6M": case "6N": case "6S": return 4.72;
                case "QG": return 2.52;
                case "QM": return 3.92;
                case "NG": case "RB": return 4.52;
                case "GF": case "HE": case "LE": case "ZL": case "ZM": case "ZS": case "ZW": return 5.72;
                case "PL": return 4.62;
                case "HG": case "SI": return 4.62;
                default: return CommissionPerContractRoundTrip;
            }
        }

        private ComboBox Accounts(string caption)
        {
            ComboBox b = Combo(TradableAccounts().Select(a=>a.Name).Distinct().OrderBy(x=>x).ToArray());
            b.Tag=caption; b.SelectionChanged += (s,e)=>{ AttachAccountEvents(); Refresh(); }; return b;
        }
        private void Submit(bool reverse)
        {
            try
            {
                Account a=Selected(accountABox), b=Selected(accountBBox); int q=Int(qtyBox,Quantity), sl=Int(slBox,StopLossTicks), tp=Int(tpBox,ProfitTargetTicks);
                DebugLog("Submit | A={0} B={1} Reverse={2} Qty={3} SL={4} TP={5} Mode={6} Price={7}", a == null ? "null" : a.Name, b == null ? "null" : b.Name, reverse, q, sl, tp, modeBox.SelectedIndex == 0 ? "MARKET" : "LIMIT/AUTO", entryBox == null ? "" : entryBox.Text);
                if (RiskPreset == HedgeFundingPreset.BulenoxQualification25KTrailing && q > EffectiveMaxContracts())
                    throw new Exception("Bulenox Qualification 25K: máximo " + EffectiveMaxContracts() + " contratos para " + Instrument.MasterInstrument.Name + ".");
                if(a==null || b==null || a==b) throw new Exception("Selecciona dos cuentas diferentes.");
                Hedge(a, reverse?OrderAction.SellShort:OrderAction.Buy, q, sl, tp, modeBox); Hedge(b, reverse?OrderAction.Buy:OrderAction.SellShort, q, sl, tp, modeBox);
                statusText.Text="ORDERS SUBMITTED · HEDGE ACTIVE";
            } catch(Exception ex) { statusText.Text="ERROR · "+ex.Message; DebugLog("Submit ERROR | {0}\n{1}", ex.Message, ex.StackTrace); }
        }
        private void Hedge(Account acc, OrderAction action, int q, int sl, int tp, ComboBox modeBox)
        {
            double panelPrice = 0;
            bool hasPanelPrice = entryBox != null && double.TryParse(entryBox.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out panelPrice) && panelPrice > 0;
            double refPrice = hasPanelPrice ? Round(panelPrice) : EntryPrice > 0 ? Round(EntryPrice) : Round(action == OrderAction.Buy || action == OrderAction.BuyToCover ? GetCurrentAsk() : GetCurrentBid());
            HedgeEntryMode m=modeBox.SelectedIndex == 0 ? HedgeEntryMode.Market : HedgeEntryMode.Limit;
            bool buy = action == OrderAction.Buy || action == OrderAction.BuyToCover;
            OrderType type;
            if (m == HedgeEntryMode.Market)
                type = OrderType.Market;
            else
            {
                // In LIMIT mode, automatically choose the valid side of the
                // market: buy above Ask / sell below Bid becomes a stop;
                // otherwise it remains a limit order.
                bool becomesStop = buy ? refPrice > GetCurrentAsk() : refPrice < GetCurrentBid();
                type = becomesStop ? OrderType.StopMarket : OrderType.Limit;
            }
            double limit = type == OrderType.Limit || type == OrderType.StopLimit ? refPrice : 0;
            double stop = type == OrderType.StopMarket || type == OrderType.StopLimit ? refPrice : 0;
            DebugLog("Order | Account={0} Action={1} Type={2} Qty={3} Limit={4} Stop={5} Ref={6} Bid={7} Ask={8}", acc.Name, action, type, q, limit, stop, refPrice, GetCurrentBid(), GetCurrentAsk());
            Order o=acc.CreateOrder(Instrument, action, type, OrderEntry.Manual, TimeInForce.Day, q, limit, stop, Guid.NewGuid().ToString("N"), "HFP-Entry", DateTime.MaxValue, null); acc.Submit(new[]{o});
            DebugLog("Order submitted | Account={0} Name={1} State={2}", acc.Name, o.Name, o.OrderState);
        }
        private void CancelOrders(){ foreach(Account a in new[]{Selected(accountABox),Selected(accountBBox)}.Where(x=>x!=null).Distinct()) try{ Order[] orders=a.Orders.Where(o=>o.Instrument==Instrument&&o.OrderState==OrderState.Working).ToArray(); DebugLog("Cancel | Account={0} Orders={1}",a.Name,orders.Length); a.Cancel(orders); }catch(Exception ex){ DebugLog("Cancel ERROR | {0}",ex.Message); } statusText.Text="CANCEL REQUESTED"; }
        private void ClosePositions()
        {
            foreach(Account a in new[]{Selected(accountABox),Selected(accountBBox)}.Where(x=>x!=null).Distinct())
            {
                try
                {
                    Order[] working = a.Orders.Where(o => o.Instrument == Instrument && o.OrderState == OrderState.Working).ToArray();
                    if (working.Length > 0) a.Cancel(working);
                    foreach (NinjaTrader.Cbi.Position position in a.Positions.Where(p => p.Instrument == Instrument && p.MarketPosition != MarketPosition.Flat))
                    {
                        OrderAction action = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
                        int quantity = Math.Abs(position.Quantity);
                        DebugLog("Close position | Account={0} Action={1} Qty={2} Instrument={3}", a.Name, action, quantity, Instrument.FullName);
                        Order closeOrder = a.CreateOrder(Instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Day, quantity, 0, 0, Guid.NewGuid().ToString("N"), "HFP-Close", DateTime.MaxValue, null);
                        a.Submit(new[]{closeOrder});
                    }
                }
                catch(Exception ex) { DebugLog("Close ERROR | Account={0} {1}", a.Name, ex.Message); }
            }
            statusText.Text="CLOSE ORDERS SENT · STRATEGY ACTIVE";
        }
        private void Refresh(){ if(quoteText==null)return; double tv=TickSize*Instrument.MasterInstrument.PointValue; int shownQty=Int(qtyBox,Quantity); double commission=GetCommissionPerContractRoundTrip()*shownQty; double slGross=Int(slBox,StopLossTicks)*tv*shownQty; double tpGross=Int(tpBox,ProfitTargetTicks)*tv*shownQty; accountText.Text="A  "+(Selected(accountABox)?.Name??"NOT SELECTED")+"   ⇄   B  "+(Selected(accountBBox)?.Name??"NOT SELECTED"); quoteText.Text=string.Format("BID {0:0.########}   ASK {1:0.########}",GetCurrentBid(),GetCurrentAsk()); riskText.Text=string.Format("SL -${0:N2}   ·   TP +${1:N2} NET   ·   {2} ticks · COMM ${3:N2}",slGross+commission,tpGross-commission,Int(tpBox,ProfitTargetTicks),commission); presetText.Text=RiskPreset == HedgeFundingPreset.BulenoxQualification25KTrailing ? string.Format("BULENOX QUALIFICATION 25K TRAILING · MAX {0} CONTRACTS · TARGET ${1:N0} · DDW ${2:N0}",EffectiveMaxContracts(),ProfitObjectiveDollars,TrailingDrawdownDollars) : "CUSTOM RISK PRESET"; }
        private Account Selected(ComboBox b)=>b?.SelectedItem==null?null:TradableAccounts().FirstOrDefault(a=>a.Name==(string)b.SelectedItem);
        private static int Int(TextBox b,int d){int x;return b!=null&&int.TryParse(b.Text,out x)&&x>0?x:d;} private double Round(double p)=>Instrument.MasterInstrument.RoundToTickSize(p);
        private void RemovePanel(){if(ChartControl==null)return;ChartControl.Dispatcher.InvokeAsync(()=>{foreach(Account account in monitoredAccounts) account.ExecutionUpdate -= OnAccountExecutionUpdate; monitoredAccounts.Clear(); if(timer!=null)timer.Stop();if(chartTraderGrid!=null&&host!=null&&chartTraderGrid.Children.Contains(host))chartTraderGrid.Children.Remove(host);host=null;body=null;chartTraderGrid=null;chartTrader=null;chartWindow=null;presetBox=null;});}
        private ComboBox Combo(string[] items){var b=new ComboBox{ItemsSource=items,Height=26,Margin=new Thickness(0,3,0,5),Background=Brush(30,41,59),Foreground=Brushes.White};if(items.Length>0)b.SelectedIndex=0;return b;}
        private TextBox Input(string s)
        {
            TextBox box = new TextBox
            {
                Text = s,
                Height = 25,
                Margin = new Thickness(0, 2, 0, 4),
                Background = Brush(30, 41, 59),
                Foreground = Brushes.White,
                BorderBrush = Brush(71, 85, 105),
                IsReadOnly = false,
                IsEnabled = true,
                IsHitTestVisible = true,
                Focusable = true,
                IsTabStop = true
            };
            box.PreviewMouseDown += (sender, args) =>
            {
                args.Handled = true;
                box.Focus();
                box.SelectAll();
                box.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Keyboard.Focus(box);
                    box.CaretIndex = box.Text == null ? 0 : box.Text.Length;
                }), DispatcherPriority.Input);
            };
            box.PreviewMouseUp += (sender, args) => { args.Handled = true; };
            box.PreviewKeyDown += OnNumericBoxKeyDown;
            return box;
        }

        private static void OnNumericBoxKeyDown(object sender, KeyEventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box == null) return;

            string character = null;
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
                character = (((int)e.Key - (int)Key.D0)).ToString();
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                character = (((int)e.Key - (int)Key.NumPad0)).ToString();
            else if (e.Key == Key.Decimal || e.Key == Key.OemPeriod || e.Key == Key.OemComma)
                character = ".";

            if (character != null)
            {
                ReplaceSelection(box, character);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                if (box.SelectionLength > 0) ReplaceSelection(box, "");
                else if (e.Key == Key.Back && box.CaretIndex > 0)
                    box.Select(box.CaretIndex - 1, 1);
                else if (e.Key == Key.Delete && box.CaretIndex < box.Text.Length)
                    box.Select(box.CaretIndex, 1);
                ReplaceSelection(box, "");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Home ||
                e.Key == Key.End || e.Key == Key.Tab || e.Key == Key.Enter || e.Key == Key.Escape)
            {
                // Navigation and commit keys are valid for the field, but must
                // not bubble to ChartControl's instrument search.
                e.Handled = true;
                if (e.Key == Key.Enter) box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        private static void ReplaceSelection(TextBox box, string value)
        {
            int start = box.SelectionStart;
            box.Text = box.Text.Remove(start, box.SelectionLength).Insert(start, value);
            box.CaretIndex = start + value.Length;
            box.SelectionLength = 0;
        }
        private Border Labelled(string s,Control c){var x=new StackPanel();x.Children.Add(Text(s,9,FontWeights.SemiBold,Brush(100,116,139)));x.Children.Add(c);return new Border{Child=x};}
        private Button Button(string s,Brush bg)=>new Button{Content=s,Background=bg,Foreground=Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(7,7,7,7),Margin=new Thickness(2),Cursor=Cursors.Hand};
        private static Border Card(TextBlock t,Brush bg)=>new Border{Background=bg,CornerRadius=new CornerRadius(5),Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,2,0,4),Child=t};
        private static TextBlock Text(string s,double z,FontWeight w,Brush c)=>new TextBlock{Text=s,FontFamily=new FontFamily("Segoe UI"),FontSize=z,FontWeight=w,Foreground=c,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,3)};
        private static Brush Brush(byte r,byte g,byte b)=>new SolidColorBrush(Color.FromRgb(r,g,b));
        private void DebugLog(string format, params object[] args)
        {
            if (!EnableDebugLogging) return;
            try { Print(string.Format("[HFP {0:yyyy-MM-dd HH:mm:ss.fff}] {1}", DateTime.Now, string.Format(format, args))); }
            catch { Print("[HFP] debug formatting error"); }
        }
    }
}
