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
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class BollingerBreakoutBot : Strategy
    {
        private const string LongSignalName = "BBC_Long";
        private const string ShortSignalName = "BBC_Short";

        private Bollinger bollinger;
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
        [Range(5, 300)]
        [Display(Name = "Periodo Bollinger", GroupName = "01. Bollinger", Order = 0)]
        public int BollingerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "Desviaciones Bollinger", GroupName = "01. Bollinger", Order = 1)]
        public double BollingerStdDev { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir compras", GroupName = "02. Direccion", Order = 0)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir ventas", GroupName = "02. Direccion", Order = 1)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro VWAP", GroupName = "03. VWAP", Order = 0)]
        public bool UseVwapFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Invertir filtro VWAP", GroupName = "03. VWAP", Order = 1)]
        public bool InvertVwapFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dibujar VWAP", GroupName = "03. VWAP", Order = 2)]
        public bool DrawVwap { get; set; }

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
        [Display(Name = "Mostrar Bollinger", GroupName = "07. Visual", Order = 0)]
        public bool AddBollingerToChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Pintar senales", GroupName = "07. Visual", Order = 1)]
        public bool DrawSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar estado", GroupName = "07. Visual", Order = 2)]
        public bool ShowStatusOnChart { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BollingerBreakoutBot";
                Description = "Entra cuando el precio cierra fuera de las bandas de Bollinger. Las salidas por TP, SL y tiempo se confirman siempre al cierre de vela.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = false;

                BollingerPeriod = 20;
                BollingerStdDev = 2;

                AllowLongs = true;
                AllowShorts = true;

                UseVwapFilter = false;
                InvertVwapFilter = false;
                DrawVwap = false;

                UseTimeWindow = false;
                StartHHmm = 930;
                EndHHmm = 1600;

                Quantity = 1;
                UseTakeProfit = true;
                TakeProfitTicks = 80;
                UseStopLoss = true;
                StopLossTicks = 40;
                ExitAfterBars = 10;

                CooldownBars = 0;
                FastBacktestMode = true;

                AddBollingerToChart = true;
                DrawSignals = true;
                ShowStatusOnChart = true;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = BollingerPeriod + 2;
            }
            else if (State == State.DataLoaded)
            {
                bollinger = Bollinger(BollingerStdDev, BollingerPeriod);
                startTime = HHmmToIntTime(StartHHmm);
                endTime = HHmmToIntTime(EndHHmm);
                entryBar = -1;
                entryPrice = 0;
                sessionCumPriceVolume = 0;
                sessionCumVolume = 0;
                internalVwap = 0;
                priorInternalVwap = 0;
                lastStatusText = string.Empty;

                if (AddBollingerToChart && !FastBacktestMode)
                    AddChartIndicator(bollinger);
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

            if (AllowLongs && Close[0] > bollinger.Upper[0] && PassesLongVwapFilter())
                EnterLongOnClose();
            else if (AllowShorts && Close[0] < bollinger.Lower[0] && PassesShortVwapFilter())
                EnterShortOnClose();
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

        private void EnterLongOnClose()
        {
            EnterLong(Quantity, LongSignalName);

            if (DrawSignals && !FastBacktestMode)
                Draw.ArrowUp(this, "BBC_Long_" + CurrentBar, false, 0, Low[0] - TickSize * 2, Brushes.LimeGreen);
        }

        private void EnterShortOnClose()
        {
            EnterShort(Quantity, ShortSignalName);

            if (DrawSignals && !FastBacktestMode)
                Draw.ArrowDown(this, "BBC_Short_" + CurrentBar, false, 0, High[0] + TickSize * 2, Brushes.Red);
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

            Draw.Line(this, "BBC_VWAP_" + CurrentBar, false, 1, priorInternalVwap, 0, internalVwap,
                Brushes.Gold, DashStyleHelper.Solid, 1);
        }

        private void RenderStatusIfNeeded()
        {
            if (!ShowStatusOnChart || FastBacktestMode)
                return;

            string status = BuildStatusText();
            if (status == lastStatusText)
                return;

            Draw.TextFixed(this, "BBC_Status", status, TextPosition.TopLeft, Brushes.White,
                new SimpleFont("Segoe UI", 12), Brushes.Transparent, Brushes.Black, 20);
            lastStatusText = status;
        }

        private string BuildStatusText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(Name);
            builder.AppendLine(string.Format("Bollinger: {0} / {1:0.##}", BollingerPeriod, BollingerStdDev));
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
