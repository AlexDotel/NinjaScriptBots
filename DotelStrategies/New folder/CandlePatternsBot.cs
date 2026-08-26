#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class CandlePatternsBot : Strategy
    {
        private const string LongSignalName = "CPB_Long";
        private const string ShortSignalName = "CPB_Short";

        private int startTime;
        private int endTime;
        private int entryBar;
        private double entryPrice;
        private double sessionCumPriceVolume;
        private double sessionCumVolume;
        private double internalVwap;
        private double priorInternalVwap;
        private string lastStatusText;

        [NinjaScriptProperty]
        [Display(Name = "Big Ass Candle", GroupName = "01. Patrones", Order = 0)]
        public bool UseBigAssCandle { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "Cuerpo minimo BAC (ticks)", GroupName = "01. Patrones", Order = 1)]
        public int BigAssMinBodyTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir compras", GroupName = "02. Direccion", Order = 0)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir ventas", GroupName = "02. Direccion", Order = 1)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "03. Horario", Order = 0)]
        public bool UseTimeWindow { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Inicio (HHmm)", GroupName = "03. Horario", Order = 1)]
        public int StartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Fin (HHmm)", GroupName = "03. Horario", Order = 2)]
        public int EndHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro VWAP", GroupName = "04. VWAP", Order = 0)]
        public bool UseVwapFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Invertir filtro VWAP", GroupName = "04. VWAP", Order = 1)]
        public bool InvertVwapFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dibujar VWAP", GroupName = "04. VWAP", Order = 2)]
        public bool DrawVwap { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos", GroupName = "05. Gestion", Order = 0)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar take profit cierre", GroupName = "05. Gestion", Order = 1)]
        public bool UseTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Take profit cierre (ticks)", GroupName = "05. Gestion", Order = 2)]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar stop loss cierre", GroupName = "05. Gestion", Order = 3)]
        public bool UseStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Stop loss cierre (ticks)", GroupName = "05. Gestion", Order = 4)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Salida tras X velas", GroupName = "05. Gestion", Order = 5)]
        public int ExitAfterBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Cooldown entradas (velas)", GroupName = "06. Backtest", Order = 0)]
        public int CooldownBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo rapido backtest", GroupName = "06. Backtest", Order = 1)]
        public bool FastBacktestMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Pintar senales", GroupName = "07. Visual", Order = 0)]
        public bool DrawSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar estado", GroupName = "07. Visual", Order = 1)]
        public bool ShowStatusOnChart { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CandlePatternsBot";
                Description = "Bot modular para operar patrones de velas seleccionables por checks. Incluye Big Ass Candle.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                IncludeTradeHistoryInBacktest = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                TraceOrders = false;

                UseBigAssCandle = true;
                BigAssMinBodyTicks = 0;

                AllowLongs = true;
                AllowShorts = true;

                UseTimeWindow = false;
                StartHHmm = 930;
                EndHHmm = 1600;

                UseVwapFilter = false;
                InvertVwapFilter = false;
                DrawVwap = false;

                Quantity = 1;
                UseTakeProfit = true;
                TakeProfitTicks = 80;
                UseStopLoss = true;
                StopLossTicks = 40;
                ExitAfterBars = 10;

                CooldownBars = 0;
                FastBacktestMode = true;

                DrawSignals = true;
                ShowStatusOnChart = false;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = 4;
            }
            else if (State == State.DataLoaded)
            {
                startTime = HHmmToIntTime(StartHHmm);
                endTime = HHmmToIntTime(EndHHmm);
                entryBar = -1;
                entryPrice = 0;
                sessionCumPriceVolume = 0;
                sessionCumVolume = 0;
                internalVwap = 0;
                priorInternalVwap = 0;
                lastStatusText = string.Empty;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            UpdateInternalVwap();

            if (CurrentBar < BarsRequiredToTrade)
                return;

            DrawVwapIfNeeded();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManageOpenPositionOnClose();
                RenderStatusIfNeeded();
                return;
            }

            RenderStatusIfNeeded();

            if (!IsInsideTimeWindow(Time[0]))
                return;

            if (entryBar >= 0 && CurrentBar - entryBar <= CooldownBars)
                return;

            string patternName;

            if (AllowLongs && HasLongPattern(out patternName) && PassesLongVwapFilter())
                EnterLongOnClose(patternName);
            else if (AllowShorts && HasShortPattern(out patternName) && PassesShortVwapFilter())
                EnterShortOnClose(patternName);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            if (execution.Order.Name == LongSignalName || execution.Order.Name == ShortSignalName)
            {
                entryBar = CurrentBar;
                entryPrice = execution.Price;
            }
        }

        private bool HasLongPattern(out string patternName)
        {
            if (UseBigAssCandle && IsBigAssCandleLong())
            {
                patternName = "Big Ass Candle";
                return true;
            }

            patternName = string.Empty;
            return false;
        }

        private bool HasShortPattern(out string patternName)
        {
            if (UseBigAssCandle && IsBigAssCandleShort())
            {
                patternName = "Big Ass Candle";
                return true;
            }

            patternName = string.Empty;
            return false;
        }

        private bool IsBigAssCandleLong()
        {
            if (!IsBearish(3) || !IsBearish(2) || !IsBearish(1) || !IsBullish(0))
                return false;

            double priorBodiesLow = Math.Min(Math.Min(Math.Min(Open[3], Close[3]), Math.Min(Open[2], Close[2])), Math.Min(Open[1], Close[1]));
            double priorBodiesHigh = Math.Max(Math.Max(Math.Max(Open[3], Close[3]), Math.Max(Open[2], Close[2])), Math.Max(Open[1], Close[1]));
            double signalBodyTicks = Math.Abs(Close[0] - Open[0]) / TickSize;

            return signalBodyTicks >= BigAssMinBodyTicks
                && Open[0] <= priorBodiesLow
                && Close[0] >= priorBodiesHigh;
        }

        private bool IsBigAssCandleShort()
        {
            if (!IsBullish(3) || !IsBullish(2) || !IsBullish(1) || !IsBearish(0))
                return false;

            double priorBodiesLow = Math.Min(Math.Min(Math.Min(Open[3], Close[3]), Math.Min(Open[2], Close[2])), Math.Min(Open[1], Close[1]));
            double priorBodiesHigh = Math.Max(Math.Max(Math.Max(Open[3], Close[3]), Math.Max(Open[2], Close[2])), Math.Max(Open[1], Close[1]));
            double signalBodyTicks = Math.Abs(Open[0] - Close[0]) / TickSize;

            return signalBodyTicks >= BigAssMinBodyTicks
                && Open[0] >= priorBodiesHigh
                && Close[0] <= priorBodiesLow;
        }

        private bool IsBullish(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsBearish(int barsAgo)
        {
            return Close[barsAgo] < Open[barsAgo];
        }

        private void EnterLongOnClose(string patternName)
        {
            EnterLong(Quantity, LongSignalName);
            DrawSignal(true, patternName);
        }

        private void EnterShortOnClose(string patternName)
        {
            EnterShort(Quantity, ShortSignalName);
            DrawSignal(false, patternName);
        }

        private void ManageOpenPositionOnClose()
        {
            if (entryBar < 0 || entryPrice <= 0)
                return;

            int barsInTrade = CurrentBar - entryBar;
            double pnlTicks = GetOpenProfitTicks();

            if (UseTakeProfit && pnlTicks >= TakeProfitTicks)
                ExitCurrentPosition("TP_Cierre");
            else if (UseStopLoss && pnlTicks <= -StopLossTicks)
                ExitCurrentPosition("SL_Cierre");
            else if (barsInTrade >= ExitAfterBars)
                ExitCurrentPosition("Tiempo_Cierre");
        }

        private void UpdateInternalVwap()
        {
            priorInternalVwap = internalVwap;

            if (Bars.IsFirstBarOfSession)
            {
                sessionCumPriceVolume = 0;
                sessionCumVolume = 0;
                priorInternalVwap = 0;
            }

            double volume = Math.Max(Volume[0], 0);
            if (volume <= 0)
                return;

            double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;
            sessionCumPriceVolume += typicalPrice * volume;
            sessionCumVolume += volume;
            internalVwap = sessionCumVolume > 0 ? sessionCumPriceVolume / sessionCumVolume : 0;
        }

        private bool PassesLongVwapFilter()
        {
            if (!UseVwapFilter)
                return true;

            if (internalVwap <= 0)
                return false;

            return InvertVwapFilter ? Close[0] < internalVwap : Close[0] > internalVwap;
        }

        private bool PassesShortVwapFilter()
        {
            if (!UseVwapFilter)
                return true;

            if (internalVwap <= 0)
                return false;

            return InvertVwapFilter ? Close[0] > internalVwap : Close[0] < internalVwap;
        }

        private double GetOpenProfitTicks()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return (Close[0] - entryPrice) / TickSize;

            if (Position.MarketPosition == MarketPosition.Short)
                return (entryPrice - Close[0]) / TickSize;

            return 0;
        }

        private void ExitCurrentPosition(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(reason, LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(reason, ShortSignalName);
        }

        private bool IsInsideTimeWindow(DateTime time)
        {
            if (!UseTimeWindow)
                return true;

            int now = ToHHmmss(time);
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

        private int ToHHmmss(DateTime time)
        {
            return time.Hour * 10000 + time.Minute * 100 + time.Second;
        }

        private void DrawVwapIfNeeded()
        {
            if (!DrawVwap || FastBacktestMode || CurrentBar < 1 || internalVwap <= 0 || priorInternalVwap <= 0)
                return;

            Draw.Line(this, "CPB_VWAP_" + CurrentBar, false, 1, priorInternalVwap, 0, internalVwap,
                Brushes.Gold, DashStyleHelper.Solid, 1);
        }

        private void DrawSignal(bool isLong, string patternName)
        {
            if (!DrawSignals || FastBacktestMode)
                return;

            if (isLong)
                Draw.ArrowUp(this, "CPB_Long_" + CurrentBar, false, 0, Low[0] - TickSize * 2, Brushes.LimeGreen);
            else
                Draw.ArrowDown(this, "CPB_Short_" + CurrentBar, false, 0, High[0] + TickSize * 2, Brushes.Red);

            Draw.Text(this, "CPB_Text_" + CurrentBar, patternName, 0,
                isLong ? Low[0] - TickSize * 6 : High[0] + TickSize * 6,
                isLong ? Brushes.LimeGreen : Brushes.Red);
        }

        private void RenderStatusIfNeeded()
        {
            if (!ShowStatusOnChart || FastBacktestMode)
                return;

            string status = BuildStatusText();
            if (status == lastStatusText)
                return;

            Draw.TextFixed(this, "CPB_Status", status, TextPosition.TopLeft, Brushes.White,
                new SimpleFont("Segoe UI", 12), Brushes.Transparent, Brushes.Black, 20);
            lastStatusText = status;
        }

        private string BuildStatusText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(Name);
            builder.AppendLine(string.Format("Patrones: BAC {0}", UseBigAssCandle ? "ON" : "OFF"));
            builder.AppendLine(string.Format("Direccion: compras {0} | ventas {1}", AllowLongs ? "ON" : "OFF", AllowShorts ? "ON" : "OFF"));
            builder.AppendLine(string.Format("Horario: {0}", IsInsideTimeWindow(Time[0]) ? "OK" : "FUERA"));
            builder.AppendLine(string.Format("VWAP: {0:0.00} | Filtro: {1}{2}", internalVwap, UseVwapFilter ? "ON" : "OFF", InvertVwapFilter ? " invertido" : ""));
            builder.AppendLine(string.Format("TP/SL cierre: {0}/{1}",
                UseTakeProfit ? TakeProfitTicks + " ticks" : "OFF",
                UseStopLoss ? StopLossTicks + " ticks" : "OFF"));

            if (Position.MarketPosition != MarketPosition.Flat && entryBar >= 0)
                builder.AppendLine(string.Format("Velas en trade: {0}/{1} | PnL cierre: {2:0.##} ticks", CurrentBar - entryBar, ExitAfterBars, GetOpenProfitTicks()));

            return builder.ToString();
        }
    }
}
