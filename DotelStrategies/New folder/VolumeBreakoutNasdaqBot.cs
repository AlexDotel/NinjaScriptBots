#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
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
    public class VolumeBreakoutNasdaqBot : Strategy
    {
        private const string LongSignalName = "VBN_Long";
        private const string ShortSignalName = "VBN_Short";

        private VOL volumeIndicator;
        private SMA volumeAverage;
        private EMA fastEma;
        private EMA slowEma;
        private ADX adx;
        private ATR atr;
        private TimeZoneInfo easternTimeZone;

        private int lastEntryBar;
        private int lastProcessedTradeCount;
        private int currentTradingDayKey;
        private double dailyRealizedPnL;
        private bool dailyLimitReached;
        private string lastStatusText;

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "Lookback breakout", GroupName = "01. Breakout", Order = 0)]
        public int BreakoutLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entrar solo al cierre", GroupName = "01. Breakout", Order = 1)]
        public bool EnterOnlyOnCloseBreakout { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "Media volumen", GroupName = "02. Volumen", Order = 0)]
        public int VolumeAveragePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Multiplicador volumen", GroupName = "02. Volumen", Order = 1)]
        public double VolumeSpikeMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir volumen creciente", GroupName = "02. Volumen", Order = 2)]
        public bool RequireRisingVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir largos", GroupName = "03. Direccion", Order = 0)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir cortos", GroupName = "03. Direccion", Order = 1)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro tendencial", GroupName = "04. Tendencia", Order = 0)]
        public bool UseTrendFilter { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "EMA rapida", GroupName = "04. Tendencia", Order = 1)]
        public int FastEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5, 400)]
        [Display(Name = "EMA lenta", GroupName = "04. Tendencia", Order = 2)]
        public int SlowEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Velas pendiente EMA lenta", GroupName = "04. Tendencia", Order = 3)]
        public int TrendSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Pendiente minima (ticks)", GroupName = "04. Tendencia", Order = 4)]
        public int MinSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "Periodo ADX", GroupName = "04. Tendencia", Order = 5)]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX minimo", GroupName = "04. Tendencia", Order = 6)]
        public double MinAdx { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar horario Eastern", GroupName = "05. Horario", Order = 0)]
        public bool UseEasternTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "05. Horario", Order = 1)]
        public bool UseTimeWindow { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Inicio (HHmm)", GroupName = "05. Horario", Order = 2)]
        public int StartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Fin (HHmm)", GroupName = "05. Horario", Order = 3)]
        public int EndHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Stop loss (ticks)", GroupName = "06. Riesgo", Order = 0)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Take profit (ticks)", GroupName = "06. Riesgo", Order = 1)]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos", GroupName = "06. Riesgo", Order = 2)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Cooldown tras entrada (velas)", GroupName = "06. Riesgo", Order = 3)]
        public int CooldownBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar limite diario", GroupName = "07. Limites diarios", Order = 0)]
        public bool UseDailyLimits { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Ganancia diaria max ($)", GroupName = "07. Limites diarios", Order = 1)]
        public double DailyProfitLimitCurrency { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Perdida diaria max ($)", GroupName = "07. Limites diarios", Order = 2)]
        public double DailyLossLimitCurrency { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contar flotante en limite diario", GroupName = "07. Limites diarios", Order = 3)]
        public bool IncludeUnrealizedInDailyLimit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cerrar posicion al tocar limite", GroupName = "07. Limites diarios", Order = 4)]
        public bool FlattenOnDailyLimit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dibujar niveles", GroupName = "08. Visual", Order = 0)]
        public bool DrawBreakoutLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Pintar senales", GroupName = "08. Visual", Order = 1)]
        public bool DrawSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar estado", GroupName = "08. Visual", Order = 2)]
        public bool ShowStatusOnChart { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "VolumeBreakoutNasdaqBot";
                Description = "Breakout de precio confirmado por expansion de volumen, filtro tendencial EMA/ADX, horario Nasdaq 09:30-15:30 Eastern y limites diarios.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = false;

                BreakoutLookback = 20;
                EnterOnlyOnCloseBreakout = true;

                VolumeAveragePeriod = 30;
                VolumeSpikeMultiplier = 1.8;
                RequireRisingVolume = true;

                AllowLongs = true;
                AllowShorts = true;

                UseTrendFilter = true;
                FastEmaPeriod = 21;
                SlowEmaPeriod = 100;
                TrendSlopeBars = 8;
                MinSlopeTicks = 4;
                AdxPeriod = 14;
                MinAdx = 18;

                UseEasternTime = true;
                UseTimeWindow = true;
                StartHHmm = 930;
                EndHHmm = 1530;

                StopLossTicks = 48;
                TakeProfitTicks = 96;
                Quantity = 1;
                CooldownBars = 5;

                UseDailyLimits = true;
                DailyProfitLimitCurrency = 800;
                DailyLossLimitCurrency = 600;
                IncludeUnrealizedInDailyLimit = true;
                FlattenOnDailyLimit = true;

                DrawBreakoutLevels = true;
                DrawSignals = true;
                ShowStatusOnChart = true;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = Math.Max(Math.Max(BreakoutLookback, VolumeAveragePeriod), Math.Max(SlowEmaPeriod, AdxPeriod)) + TrendSlopeBars + 2;
                SetStopLoss(LongSignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(LongSignalName, CalculationMode.Ticks, TakeProfitTicks);
                SetStopLoss(ShortSignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(ShortSignalName, CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                volumeIndicator = VOL();
                volumeAverage = SMA(volumeIndicator, VolumeAveragePeriod);
                fastEma = EMA(Close, FastEmaPeriod);
                slowEma = EMA(Close, SlowEmaPeriod);
                adx = ADX(AdxPeriod);
                atr = ATR(14);
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                lastEntryBar = -1000000;
                lastProcessedTradeCount = 0;
                currentTradingDayKey = -1;
                dailyRealizedPnL = 0;
                dailyLimitReached = false;
                lastStatusText = string.Empty;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            DateTime tradingTime = GetTradingTime(Time[0]);
            ResetDailyCountersIfNeeded(tradingTime);
            ProcessClosedTrades();

            double currentDailyPnL = GetCurrentDailyPnL();
            bool insideTimeWindow = !UseTimeWindow || IsInsideTimeWindow(tradingTime);
            CheckDailyLimit(currentDailyPnL);

            double breakoutHigh = MAX(High, BreakoutLookback)[1];
            double breakoutLow = MIN(Low, BreakoutLookback)[1];

            if (DrawBreakoutLevels)
                DrawBreakoutReferenceLines(breakoutHigh, breakoutLow);

            RenderStatusIfNeeded(tradingTime, insideTimeWindow, currentDailyPnL, breakoutHigh, breakoutLow);

            if (dailyLimitReached)
            {
                ExitOpenPositionIfNeeded();
                return;
            }

            if (!insideTimeWindow || Position.MarketPosition != MarketPosition.Flat)
                return;

            if (CooldownBars > 0 && CurrentBar - lastEntryBar < CooldownBars)
                return;

            bool volumeBreakout = IsVolumeBreakout();
            bool longBreakout = AllowLongs && volumeBreakout && IsLongPriceBreakout(breakoutHigh) && IsLongTrendOk();
            bool shortBreakout = AllowShorts && volumeBreakout && IsShortPriceBreakout(breakoutLow) && IsShortTrendOk();

            if (longBreakout)
            {
                EnterLong(Quantity, LongSignalName);
                RegisterEntry(true);
            }
            else if (shortBreakout)
            {
                EnterShort(Quantity, ShortSignalName);
                RegisterEntry(false);
            }
        }

        private bool IsVolumeBreakout()
        {
            bool spike = volumeIndicator[0] >= volumeAverage[0] * VolumeSpikeMultiplier;
            bool rising = !RequireRisingVolume || volumeIndicator[0] > volumeIndicator[1];
            return spike && rising;
        }

        private bool IsLongPriceBreakout(double breakoutHigh)
        {
            if (EnterOnlyOnCloseBreakout)
                return Close[0] > breakoutHigh && Close[1] <= breakoutHigh;

            return High[0] > breakoutHigh && High[1] <= breakoutHigh;
        }

        private bool IsShortPriceBreakout(double breakoutLow)
        {
            if (EnterOnlyOnCloseBreakout)
                return Close[0] < breakoutLow && Close[1] >= breakoutLow;

            return Low[0] < breakoutLow && Low[1] >= breakoutLow;
        }

        private bool IsLongTrendOk()
        {
            if (!UseTrendFilter)
                return true;

            double slopeTicks = (slowEma[0] - slowEma[TrendSlopeBars]) / TickSize;
            return Close[0] > slowEma[0]
                && fastEma[0] > slowEma[0]
                && slopeTicks >= MinSlopeTicks
                && adx[0] >= MinAdx;
        }

        private bool IsShortTrendOk()
        {
            if (!UseTrendFilter)
                return true;

            double slopeTicks = (slowEma[TrendSlopeBars] - slowEma[0]) / TickSize;
            return Close[0] < slowEma[0]
                && fastEma[0] < slowEma[0]
                && slopeTicks >= MinSlopeTicks
                && adx[0] >= MinAdx;
        }

        private void RegisterEntry(bool isLong)
        {
            lastEntryBar = CurrentBar;

            if (!DrawSignals)
                return;

            string tag = string.Format("VBN_{0}_{1}", isLong ? "L" : "S", CurrentBar);
            Brush brush = isLong ? Brushes.LimeGreen : Brushes.OrangeRed;

            if (isLong)
                Draw.ArrowUp(this, tag, false, 0, Low[0] - 2 * TickSize, brush);
            else
                Draw.ArrowDown(this, tag, false, 0, High[0] + 2 * TickSize, brush);
        }

        private void ProcessClosedTrades()
        {
            int tradeCount = SystemPerformance.AllTrades.Count;

            for (int index = lastProcessedTradeCount; index < tradeCount; index++)
            {
                Trade trade = SystemPerformance.AllTrades[index];

                if (trade == null || trade.Exit == null)
                    continue;

                DateTime exitTradingTime = GetTradingTime(trade.Exit.Time);
                if (ToDay(exitTradingTime) == currentTradingDayKey)
                    dailyRealizedPnL += trade.ProfitCurrency;
            }

            lastProcessedTradeCount = tradeCount;
        }

        private double GetCurrentDailyPnL()
        {
            double pnl = dailyRealizedPnL;

            if (IncludeUnrealizedInDailyLimit && Position.MarketPosition != MarketPosition.Flat)
                pnl += Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

            return pnl;
        }

        private void CheckDailyLimit(double currentDailyPnL)
        {
            if (!UseDailyLimits)
                return;

            bool profitLimitHit = DailyProfitLimitCurrency > 0 && currentDailyPnL >= DailyProfitLimitCurrency;
            bool lossLimitHit = DailyLossLimitCurrency > 0 && currentDailyPnL <= -DailyLossLimitCurrency;

            if (profitLimitHit || lossLimitHit)
                dailyLimitReached = true;
        }

        private void ExitOpenPositionIfNeeded()
        {
            if (!FlattenOnDailyLimit)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("DailyLimitExitLong", LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("DailyLimitExitShort", ShortSignalName);
        }

        private void ResetDailyCountersIfNeeded(DateTime tradingTime)
        {
            int dayKey = ToDay(tradingTime);

            if (currentTradingDayKey == dayKey)
                return;

            currentTradingDayKey = dayKey;
            dailyRealizedPnL = 0;
            dailyLimitReached = false;
        }

        private DateTime GetTradingTime(DateTime barTime)
        {
            if (!UseEasternTime)
                return barTime;

            return TimeZoneInfo.ConvertTime(barTime, TimeZoneInfo.Local, easternTimeZone);
        }

        private bool IsInsideTimeWindow(DateTime tradingTime)
        {
            int now = ToHHmmss(tradingTime);
            int start = HHmmToIntTime(StartHHmm);
            int end = HHmmToIntTime(EndHHmm);

            if (start <= end)
                return now >= start && now <= end;

            return now >= start || now <= end;
        }

        private int ToHHmmss(DateTime time)
        {
            return time.Hour * 10000 + time.Minute * 100 + time.Second;
        }

        private int HHmmToIntTime(int hhmm)
        {
            int hours = Math.Max(0, Math.Min(23, hhmm / 100));
            int minutes = Math.Max(0, Math.Min(59, hhmm % 100));
            return hours * 10000 + minutes * 100;
        }

        private void DrawBreakoutReferenceLines(double breakoutHigh, double breakoutLow)
        {
            Draw.HorizontalLine(this, "VBN_CurrentHigh", breakoutHigh, Brushes.DodgerBlue);
            Draw.HorizontalLine(this, "VBN_CurrentLow", breakoutLow, Brushes.OrangeRed);
        }

        private void RenderStatusIfNeeded(DateTime tradingTime, bool insideTimeWindow, double currentDailyPnL, double breakoutHigh, double breakoutLow)
        {
            if (!ShowStatusOnChart)
                return;

            double volumeRatio = volumeAverage[0] > 0 ? volumeIndicator[0] / volumeAverage[0] : 0;
            string trendState = BuildTrendState();

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("VOLUME BREAKOUT NASDAQ BOT");
            builder.AppendLine(string.Format("Hora: {0:HH:mm:ss} {1} | Horario: {2}", tradingTime, UseEasternTime ? "ET" : "LOCAL", insideTimeWindow ? "ACTIVO" : "FUERA"));
            builder.AppendLine(string.Format("Breakout H/L: {0:N2} / {1:N2} | Lookback: {2}", breakoutHigh, breakoutLow, BreakoutLookback));
            builder.AppendLine(string.Format("Vol: {0:N0} | Media: {1:N0} | Ratio: {2:N2}x / {3:N2}x", volumeIndicator[0], volumeAverage[0], volumeRatio, VolumeSpikeMultiplier));
            builder.AppendLine(string.Format("Trend: {0} | EMA {1}/{2} | ADX: {3:N1}", trendState, FastEmaPeriod, SlowEmaPeriod, adx[0]));
            builder.AppendLine(string.Format("ATR14: {0:N2} | SL/TP: {1}t/{2}t | Pos: {3}", atr[0], StopLossTicks, TakeProfitTicks, Position.MarketPosition));
            builder.Append(string.Format("PnL dia: ${0:N2} | Limite: {1}", currentDailyPnL, dailyLimitReached ? "TOCADO" : "OK"));

            string statusText = builder.ToString();
            if (statusText == lastStatusText)
                return;

            Draw.TextFixed(
                this,
                "VBN_Status",
                statusText,
                TextPosition.TopLeft,
                Brushes.White,
                new SimpleFont("Consolas", 12),
                Brushes.Black,
                Brushes.Black,
                70);

            lastStatusText = statusText;
        }

        private string BuildTrendState()
        {
            if (!UseTrendFilter)
                return "OFF";

            if (IsLongTrendOk())
                return "ALCISTA";

            if (IsShortTrendOk())
                return "BAJISTA";

            return "NEUTRA";
        }
    }
}
