#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class WaywardMeanReversionNinjaBot : Strategy
    {
        private const string LongSignalName = "Wayward_Long";
        private const string ShortSignalName = "Wayward_Short";

        private Bollinger bollinger;
        private Bollinger higherBollinger;
        private RSI rsi;
        private ATR atr;

        private Order longStopOrder;
        private Order shortStopOrder;
        private DateTime spikePauseUntil;
        private double previousBid;

        private int startTime;
        private int endTime;
        private int lastProcessedTradeCount;
        private int lastDayKey;
        private DateTime weekResetTime;
        private DateTime monthResetTime;

        private double initialBalance;
        private double dayStartEquity;
        private double weekStartEquity;
        private double monthStartEquity;
        private double allTimeHighEquity;
        private double dayHighEquity;
        private double weekHighEquity;
        private double monthHighEquity;
        private double realizedPnL;
        private double dayDrawdownPct;
        private double weekDrawdownPct;
        private double monthDrawdownPct;
        private double allDrawdownPct;
        private double dayProfitPct;
        private double weekProfitPct;
        private double monthProfitPct;
        private double allProfitPct;
        private bool tradingEnabled;
        private string statusText;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos", GroupName = "01. Gestion", Order = 0)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir compras", GroupName = "01. Gestion", Order = 1)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir ventas", GroupName = "01. Gestion", Order = 2)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Range(5, 300)]
        [Display(Name = "Periodo Bollinger", GroupName = "02. Indicadores", Order = 0)]
        public int BollingerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "Desviaciones Bollinger", GroupName = "02. Indicadores", Order = 1)]
        public double BollingerStdDev { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "Periodo RSI", GroupName = "02. Indicadores", Order = 2)]
        public int RsiPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Filtro RSI desde 50", GroupName = "02. Indicadores", Order = 3)]
        public double RsiFilter { get; set; }

        [NinjaScriptProperty]
        [Range(10, 10000)]
        [Display(Name = "Periodo ATR", GroupName = "02. Indicadores", Order = 4)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 10)]
        [Display(Name = "Vela minima ATR", GroupName = "03. Ordenes ATR", Order = 0)]
        public double MinCandleAtr { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 20)]
        [Display(Name = "Stop loss ATR", GroupName = "03. Ordenes ATR", Order = 1)]
        public double StopLossAtr { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 20)]
        [Display(Name = "Trailing ATR", GroupName = "03. Ordenes ATR", Order = 2)]
        public double TrailingAtr { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 20)]
        [Display(Name = "Distancia orden ATR", GroupName = "03. Ordenes ATR", Order = 3)]
        public double OrderDistanceAtr { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Spread max ticks", GroupName = "03. Ordenes ATR", Order = 4)]
        public int MaxSpreadTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "04. Horario", Order = 0)]
        public bool UseTimeWindow { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Inicio (HHmm)", GroupName = "04. Horario", Order = 1)]
        public int StartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Fin (HHmm)", GroupName = "04. Horario", Order = 2)]
        public int EndHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar confluencia HTF", GroupName = "05. Higher timeframe", Order = 0)]
        public bool UseHigherTimeframeConfluence { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "HTF minutos", GroupName = "05. Higher timeframe", Order = 1)]
        public int HigherTimeframeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(5, 300)]
        [Display(Name = "Periodo Bollinger HTF", GroupName = "05. Higher timeframe", Order = 2)]
        public int HigherBollingerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "Desviaciones HTF", GroupName = "05. Higher timeframe", Order = 3)]
        public double HigherBollingerStdDev { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar spike filter", GroupName = "06. Spike filter", Order = 0)]
        public bool UseSpikeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 10)]
        [Display(Name = "Spike ATR", GroupName = "06. Spike filter", Order = 1)]
        public double SpikeAtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3600)]
        [Display(Name = "Pausa spike segundos", GroupName = "06. Spike filter", Order = 2)]
        public int SpikePauseSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar limites prop firm", GroupName = "07. Prop firm", Order = 0)]
        public bool UsePropFirmLimits { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000000)]
        [Display(Name = "Balance inicial", GroupName = "07. Prop firm", Order = 1)]
        public double InitialBalanceInput { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Profit target %", GroupName = "07. Prop firm", Order = 2)]
        public double ProfitTargetPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max DD dia %", GroupName = "07. Prop firm", Order = 3)]
        public double MaxDrawdownDayPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max DD 7 dias %", GroupName = "07. Prop firm", Order = 4)]
        public double MaxDrawdownWeekPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max DD 30 dias %", GroupName = "07. Prop firm", Order = 5)]
        public double MaxDrawdownMonthPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max DD total %", GroupName = "07. Prop firm", Order = 6)]
        public double MaxDrawdownTotalPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cerrar posicion al tocar limite", GroupName = "07. Prop firm", Order = 7)]
        public bool FlattenOnPropLimit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar estado", GroupName = "08. Visual", Order = 0)]
        public bool ShowStatusOnChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Bollinger", GroupName = "08. Visual", Order = 1)]
        public bool AddBollingerToChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Pintar senales", GroupName = "08. Visual", Order = 2)]
        public bool DrawSignals { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "WaywardMeanReversionNinjaBot";
                Description = "Conversion NinjaTrader del EA Wayward: Bollinger + RSI mean reversion, entradas stop, ATR trailing, HTF confluence, spike filter y limites prop firm. Sin magic number.";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = false;

                Quantity = 1;
                AllowLongs = true;
                AllowShorts = true;

                BollingerPeriod = 20;
                BollingerStdDev = 2;
                RsiPeriod = 14;
                RsiFilter = 30;
                AtrPeriod = 1000;

                MinCandleAtr = 0.5;
                StopLossAtr = 1.0;
                TrailingAtr = 0.2;
                OrderDistanceAtr = 0.2;
                MaxSpreadTicks = 2;

                UseTimeWindow = false;
                StartHHmm = 930;
                EndHHmm = 1600;

                UseHigherTimeframeConfluence = true;
                HigherTimeframeMinutes = 60;
                HigherBollingerPeriod = 20;
                HigherBollingerStdDev = 2;

                UseSpikeFilter = true;
                SpikeAtrMultiplier = 0.5;
                SpikePauseSeconds = 300;

                UsePropFirmLimits = false;
                InitialBalanceInput = 50000;
                ProfitTargetPct = 0;
                MaxDrawdownDayPct = 0;
                MaxDrawdownWeekPct = 0;
                MaxDrawdownMonthPct = 0;
                MaxDrawdownTotalPct = 0;
                FlattenOnPropLimit = false;

                ShowStatusOnChart = true;
                AddBollingerToChart = true;
                DrawSignals = true;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = Math.Max(Math.Max(BollingerPeriod, RsiPeriod), AtrPeriod) + 5;
                AddDataSeries(BarsPeriodType.Minute, HigherTimeframeMinutes);
            }
            else if (State == State.DataLoaded)
            {
                bollinger = Bollinger(Close, BollingerStdDev, BollingerPeriod);
                higherBollinger = Bollinger(Closes[1], HigherBollingerStdDev, HigherBollingerPeriod);
                rsi = RSI(Close, RsiPeriod, 3);
                atr = ATR(Close, AtrPeriod);

                startTime = HHmmToIntTime(StartHHmm);
                endTime = HHmmToIntTime(EndHHmm);
                spikePauseUntil = Core.Globals.MinDate;
                previousBid = 0;
                lastProcessedTradeCount = 0;
                lastDayKey = -1;
                weekResetTime = Core.Globals.MinDate;
                monthResetTime = Core.Globals.MinDate;
                realizedPnL = 0;
                tradingEnabled = true;
                statusText = "Enabled";

                if (AddBollingerToChart)
                    AddChartIndicator(bollinger);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < HigherBollingerPeriod + 2)
                return;

            ProcessClosedTrades();
            ResetEquityWindowsIfNeeded();
            UpdatePropFirmStats();
            tradingEnabled = !UsePropFirmLimits || CheckTradingAllowed();

            double bid = GetCurrentBid();
            double ask = GetCurrentAsk();
            if (bid <= 0 || ask <= 0 || State == State.Historical)
            {
                bid = Close[0];
                ask = Close[0] + TickSize;
            }

            RenderStatusIfNeeded(bid, ask);
            ManageOpenPosition(ask, bid);
            ManageWorkingOrders(ask, bid);

            if (!tradingEnabled)
            {
                CancelWorkingOrders();
                if (FlattenOnPropLimit)
                    ExitOpenPosition();
                return;
            }

            if (!IsInsideTimeWindow(Time[0]))
            {
                CancelWorkingOrders();
                return;
            }

            if (UseSpikeFilter && IsSpikePaused(bid))
                return;

            if (Position.MarketPosition != MarketPosition.Flat || HasWorkingEntryOrder())
                return;

            int signal = GetTradeSignal(ask, bid);
            if (signal < 0 && AllowLongs)
                SubmitLongStop(ask);
            else if (signal > 0 && AllowShorts)
                SubmitShortStop(bid);
        }

        protected override void OnOrderUpdate(Order order,
                                              double limitPrice,
                                              double stopPrice,
                                              int quantity,
                                              int filled,
                                              double averageFillPrice,
                                              OrderState orderState,
                                              DateTime time,
                                              ErrorCode error,
                                              string nativeError)
        {
            if (order == null)
                return;

            if (order.Name == LongSignalName)
                longStopOrder = IsTerminalOrderState(orderState) ? null : order;
            else if (order.Name == ShortSignalName)
                shortStopOrder = IsTerminalOrderState(orderState) ? null : order;
        }

        private int GetTradeSignal(double ask, double bid)
        {
            double candleSize = High[0] - Low[0];
            if (candleSize <= atr[0] * MinCandleAtr)
                return 0;

            bool higherLongOk = !UseHigherTimeframeConfluence || bid < higherBollinger.Lower[0];
            bool higherShortOk = !UseHigherTimeframeConfluence || ask > higherBollinger.Upper[0];

            if (bid < bollinger.Lower[0] && rsi[0] < 50.0 - RsiFilter && higherLongOk)
                return -1;

            if (ask > bollinger.Upper[0] && rsi[0] > 50.0 + RsiFilter && higherShortOk)
                return 1;

            return 0;
        }

        private void SubmitLongStop(double ask)
        {
            if (!IsSpreadAllowed())
                return;

            double entry = RoundToTick(ask + atr[0] * OrderDistanceAtr);
            double stop = RoundToTick(entry - atr[0] * StopLossAtr);
            double target = RoundToTick(bollinger.Middle[0]);

            if (target <= entry)
                target = RoundToTick(entry + Math.Max(TickSize, atr[0] * TrailingAtr));

            SetStopLoss(LongSignalName, CalculationMode.Price, stop, false);
            SetProfitTarget(LongSignalName, CalculationMode.Price, target);
            EnterLongStopMarket(0, true, Quantity, entry, LongSignalName);

            if (DrawSignals)
                Draw.ArrowUp(this, "WaywardLong" + CurrentBar, false, 0, Low[0] - 2 * TickSize, Brushes.LimeGreen);
        }

        private void SubmitShortStop(double bid)
        {
            if (!IsSpreadAllowed())
                return;

            double entry = RoundToTick(bid - atr[0] * OrderDistanceAtr);
            double stop = RoundToTick(entry + atr[0] * StopLossAtr);
            double target = RoundToTick(bollinger.Middle[0]);

            if (target >= entry)
                target = RoundToTick(entry - Math.Max(TickSize, atr[0] * TrailingAtr));

            SetStopLoss(ShortSignalName, CalculationMode.Price, stop, false);
            SetProfitTarget(ShortSignalName, CalculationMode.Price, target);
            EnterShortStopMarket(0, true, Quantity, entry, ShortSignalName);

            if (DrawSignals)
                Draw.ArrowDown(this, "WaywardShort" + CurrentBar, false, 0, High[0] + 2 * TickSize, Brushes.Red);
        }

        private void ManageOpenPosition(double ask, double bid)
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double newStop = RoundToTick(bid - atr[0] * TrailingAtr);
                if (newStop > Position.AveragePrice)
                    SetStopLoss(LongSignalName, CalculationMode.Price, newStop, false);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double newStop = RoundToTick(ask + atr[0] * TrailingAtr);
                if (newStop < Position.AveragePrice)
                    SetStopLoss(ShortSignalName, CalculationMode.Price, newStop, false);
            }
        }

        private void ManageWorkingOrders(double ask, double bid)
        {
            if (longStopOrder != null && longStopOrder.OrderState == OrderState.Working)
            {
                double entry = RoundToTick(ask + atr[0] * OrderDistanceAtr);
                if (entry < longStopOrder.StopPrice)
                {
                    double stop = RoundToTick(entry - atr[0] * StopLossAtr);
                    ChangeOrder(longStopOrder, longStopOrder.Quantity, 0, entry);
                    SetStopLoss(LongSignalName, CalculationMode.Price, stop, false);
                }
            }

            if (shortStopOrder != null && shortStopOrder.OrderState == OrderState.Working)
            {
                double entry = RoundToTick(bid - atr[0] * OrderDistanceAtr);
                if (entry > shortStopOrder.StopPrice)
                {
                    double stop = RoundToTick(entry + atr[0] * StopLossAtr);
                    ChangeOrder(shortStopOrder, shortStopOrder.Quantity, 0, entry);
                    SetStopLoss(ShortSignalName, CalculationMode.Price, stop, false);
                }
            }
        }

        private bool IsSpikePaused(double bid)
        {
            if (previousBid > 0 && Math.Abs(bid - previousBid) > atr[0] * SpikeAtrMultiplier)
                spikePauseUntil = Time[0].AddSeconds(SpikePauseSeconds);

            previousBid = bid;
            return Time[0] < spikePauseUntil;
        }

        private bool IsSpreadAllowed()
        {
            if (MaxSpreadTicks <= 0)
                return true;

            double ask = GetCurrentAsk();
            double bid = GetCurrentBid();
            if (ask <= 0 || bid <= 0)
                return true;

            return (ask - bid) / TickSize <= MaxSpreadTicks;
        }

        private void ResetEquityWindowsIfNeeded()
        {
            int dayKey = Time[0].Year * 10000 + Time[0].Month * 100 + Time[0].Day;
            double currentEquity = GetStrategyEquity();

            if (initialBalance <= 0)
            {
                initialBalance = InitialBalanceInput > 0 ? InitialBalanceInput : 50000;
                dayStartEquity = initialBalance;
                weekStartEquity = initialBalance;
                monthStartEquity = initialBalance;
                allTimeHighEquity = initialBalance;
                dayHighEquity = initialBalance;
                weekHighEquity = initialBalance;
                monthHighEquity = initialBalance;
                weekResetTime = Time[0].AddDays(7);
                monthResetTime = Time[0].AddDays(30);
            }

            if (lastDayKey != dayKey)
            {
                lastDayKey = dayKey;
                dayStartEquity = currentEquity;
                dayHighEquity = currentEquity;
                dayDrawdownPct = 0;
                dayProfitPct = 0;
            }

            if (weekResetTime == Core.Globals.MinDate || Time[0] >= weekResetTime)
            {
                weekStartEquity = currentEquity;
                weekHighEquity = currentEquity;
                weekDrawdownPct = 0;
                weekProfitPct = 0;
                weekResetTime = Time[0].AddDays(7);
            }

            if (monthResetTime == Core.Globals.MinDate || Time[0] >= monthResetTime)
            {
                monthStartEquity = currentEquity;
                monthHighEquity = currentEquity;
                monthDrawdownPct = 0;
                monthProfitPct = 0;
                monthResetTime = Time[0].AddDays(30);
            }
        }

        private void UpdatePropFirmStats()
        {
            double equity = GetStrategyEquity();

            dayHighEquity = Math.Max(dayHighEquity, equity);
            weekHighEquity = Math.Max(weekHighEquity, equity);
            monthHighEquity = Math.Max(monthHighEquity, equity);
            allTimeHighEquity = Math.Max(allTimeHighEquity, equity);

            dayProfitPct = Percent(equity - dayStartEquity, dayStartEquity);
            weekProfitPct = Percent(equity - weekStartEquity, weekStartEquity);
            monthProfitPct = Percent(equity - monthStartEquity, monthStartEquity);
            allProfitPct = Percent(equity - initialBalance, initialBalance);

            dayDrawdownPct = Math.Min(dayDrawdownPct, Percent(equity - dayHighEquity, dayHighEquity));
            weekDrawdownPct = Math.Min(weekDrawdownPct, Percent(equity - weekHighEquity, weekHighEquity));
            monthDrawdownPct = Math.Min(monthDrawdownPct, Percent(equity - monthHighEquity, monthHighEquity));
            allDrawdownPct = Math.Min(allDrawdownPct, Percent(equity - allTimeHighEquity, allTimeHighEquity));
        }

        private bool CheckTradingAllowed()
        {
            if (MaxDrawdownDayPct > 0 && dayDrawdownPct < -MaxDrawdownDayPct)
                return DisableTrading("Max DD dia alcanzado");

            if (MaxDrawdownWeekPct > 0 && weekDrawdownPct < -MaxDrawdownWeekPct)
                return DisableTrading("Max DD 7 dias alcanzado");

            if (MaxDrawdownMonthPct > 0 && monthDrawdownPct < -MaxDrawdownMonthPct)
                return DisableTrading("Max DD 30 dias alcanzado");

            if (MaxDrawdownTotalPct > 0 && allDrawdownPct < -MaxDrawdownTotalPct)
                return DisableTrading("Max DD total alcanzado");

            if (ProfitTargetPct > 0 && allProfitPct > ProfitTargetPct)
                return DisableTrading("Profit target alcanzado");

            statusText = "Enabled";
            return true;
        }

        private bool DisableTrading(string reason)
        {
            statusText = reason;
            return false;
        }

        private double GetStrategyEquity()
        {
            double unrealized = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
            double baseEquity = initialBalance > 0 ? initialBalance : (InitialBalanceInput > 0 ? InitialBalanceInput : 50000);
            return baseEquity + realizedPnL + unrealized;
        }

        private void ProcessClosedTrades()
        {
            int count = SystemPerformance.AllTrades.Count;
            if (lastProcessedTradeCount > count)
                lastProcessedTradeCount = 0;

            for (int i = lastProcessedTradeCount; i < count; i++)
                realizedPnL += SystemPerformance.AllTrades[i].ProfitCurrency;

            lastProcessedTradeCount = count;
        }

        private void CancelWorkingOrders()
        {
            if (longStopOrder != null && longStopOrder.OrderState == OrderState.Working)
                CancelOrder(longStopOrder);
            if (shortStopOrder != null && shortStopOrder.OrderState == OrderState.Working)
                CancelOrder(shortStopOrder);
        }

        private void ExitOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("Wayward_PropFlatten", LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("Wayward_PropFlatten", ShortSignalName);
        }

        private bool HasWorkingEntryOrder()
        {
            return (longStopOrder != null && longStopOrder.OrderState == OrderState.Working) ||
                   (shortStopOrder != null && shortStopOrder.OrderState == OrderState.Working);
        }

        private bool IsTerminalOrderState(OrderState state)
        {
            return state == OrderState.Cancelled ||
                   state == OrderState.Filled ||
                   state == OrderState.Rejected;
        }

        private bool IsInsideTimeWindow(DateTime time)
        {
            if (!UseTimeWindow)
                return true;

            int now = ToTime(time);
            if (startTime <= endTime)
                return now >= startTime && now <= endTime;

            return now >= startTime || now <= endTime;
        }

        private int HHmmToIntTime(int hhmm)
        {
            int hours = Math.Max(0, Math.Min(23, hhmm / 100));
            int minutes = Math.Max(0, Math.Min(59, hhmm % 100));
            return hours * 10000 + minutes * 100;
        }

        private double Percent(double numerator, double denominator)
        {
            if (Math.Abs(denominator) < double.Epsilon)
                return 0;

            return numerator / denominator * 100.0;
        }

        private double RoundToTick(double price)
        {
            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private void RenderStatusIfNeeded(double bid, double ask)
        {
            if (!ShowStatusOnChart)
                return;

            string text =
                "Wayward Ninja" +
                "\nTrading: " + (tradingEnabled ? "ENABLED" : "DISABLED") +
                "\nStatus: " + statusText +
                "\nRSI: " + rsi[0].ToString("0.0") +
                "\nSpread ticks: " + ((ask - bid) / TickSize).ToString("0.0") +
                "\nProfit all: " + allProfitPct.ToString("0.00") + "%" +
                "\nDD dia/7/30/all: " + dayDrawdownPct.ToString("0.00") + "% / " +
                weekDrawdownPct.ToString("0.00") + "% / " +
                monthDrawdownPct.ToString("0.00") + "% / " +
                allDrawdownPct.ToString("0.00") + "%";

            Draw.TextFixed(this, "WaywardStatus", text, TextPosition.TopLeft, Brushes.White,
                new SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Black, 60);
        }
    }
}
