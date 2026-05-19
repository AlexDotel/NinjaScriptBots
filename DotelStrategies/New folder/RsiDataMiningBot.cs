#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class RsiDataMiningBot : Strategy
    {
        public enum TradeBias
        {
            Both,
            LongOnly,
            ShortOnly
        }

        public enum RsiEntryMode
        {
            CrossBackFromExtreme,
            ExtremeTouch,
            CenterlineCross,
            SignalLineCross,
            RecoveryFromExtreme,
            BreakoutThroughExtreme,
            SlopeReversalAfterExtreme
        }

        public enum TrendFilterMode
        {
            None,
            PriceVsSlowEma,
            FastVsSlowEma,
            SlowEmaSlope,
            StackAndSlope
        }

        public enum AdxFilterMode
        {
            None,
            Minimum,
            Maximum,
            Range
        }

        public enum PriceActionFilterMode
        {
            None,
            CandleColor,
            BreakPreviousBar,
            CandleAndBreak
        }

        public enum StopTargetMode
        {
            Ticks,
            Atr
        }

        public enum ExitMode
        {
            ProtectiveOnly,
            Midline,
            OppositeSignal,
            TimeoutBars,
            MidlineOrOpposite,
            MidlineOrTimeout,
            OppositeOrTimeout
        }

        private const string LongSignalName = "RsiDataMiningLong";
        private const string ShortSignalName = "RsiDataMiningShort";

        private RSI rsi;
        private SMA rsiSignalLine;
        private EMA fastEma;
        private EMA slowEma;
        private ATR atr;
        private ADX adx;

        private double sessionStartCumProfit;
        private bool sessionInitialized;
        private bool parametersValid;
        private int tradesThisSession;
        private int lastEntryBar;
        private int positionEntryBar;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Laboratorio RSI para mineria de datos en Strategy Analyzer. Permite explorar familias de setups RSI, filtros de contexto y salidas robustas sin disparar la complejidad.";
                Name = "RsiDataMiningBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                TraceOrders = false;
                IncludeCommission = true;
                DefaultQuantity = 1;
                Slippage = 1;
                BarsRequiredToTrade = 50;

                AllowedDirection = TradeBias.Both;
                OrderQuantity = 1;
                StartTime = 93000;
                EndTime = 160000;
                FlattenAtEndTime = true;
                MaxTradesPerSession = 8;
                MinBarsBetweenEntries = 3;
                MaxDailyLoss = 0;

                EntrySignalMode = RsiEntryMode.CrossBackFromExtreme;
                RsiPeriod = 14;
                RsiSmoothing = 3;
                SignalLinePeriod = 5;
                LowerThreshold = 30.0;
                UpperThreshold = 70.0;
                Midline = 50.0;
                UseSymmetricThresholds = true;
                MinimumLevelSeparation = 20.0;
                ExtremeLookback = 6;
                SlopeLookback = 2;
                MinimumSlopePoints = 3.0;
                RequireSignalLineAgreement = false;
                RequireExtremeTouchInLookback = false;

                TrendFilter = TrendFilterMode.None;
                FastTrendPeriod = 21;
                SlowTrendPeriod = 55;
                TrendSlopeLookback = 3;
                AdxFilter = AdxFilterMode.None;
                AdxPeriod = 14;
                MinimumAdx = 18.0;
                MaximumAdx = 35.0;
                AtrPeriod = 14;
                MinimumAtrTicks = 0.0;
                MaximumAtrTicks = 0.0;
                PriceActionFilter = PriceActionFilterMode.None;

                ProtectiveOrderMode = StopTargetMode.Atr;
                StopLossTicks = 20;
                ProfitTargetTicks = 30;
                StopLossAtrMultiplier = 1.5;
                ProfitTargetAtrMultiplier = 2.0;
                PositionExitMode = ExitMode.MidlineOrTimeout;
                MaxBarsInTrade = 12;

                lastEntryBar = -1;
                positionEntryBar = -1;
            }
            else if (State == State.Configure)
            {
                parametersValid = ValidateParameters();
                BarsRequiredToTrade = GetRequiredBarsCount();
            }
            else if (State == State.DataLoaded)
            {
                rsi = RSI(RsiPeriod, RsiSmoothing);
                rsiSignalLine = SMA(rsi, SignalLinePeriod);
                fastEma = EMA(FastTrendPeriod);
                slowEma = EMA(SlowTrendPeriod);
                atr = ATR(AtrPeriod);
                adx = ADX(AdxPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (!parametersValid)
                return;

            if (CurrentBar < BarsRequiredToTrade - 1)
                return;

            ResetSessionStatsIfNeeded();

            if (ShouldForceFlat())
            {
                ExitOpenPosition("TimeWindowExit");
                return;
            }

            if (HasReachedDailyLossLimit())
            {
                ExitOpenPosition("DailyLossExit");
                return;
            }

            ManageOpenPosition();

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (!IsWithinTradingWindow(ToTime(Time[0])))
                return;

            if (MaxTradesPerSession > 0 && tradesThisSession >= MaxTradesPerSession)
                return;

            if (lastEntryBar >= 0 && CurrentBar - lastEntryBar < Math.Max(0, MinBarsBetweenEntries))
                return;

            if (!PassAtrFilter())
                return;

            if (!PassAdxFilter())
                return;

            bool canLong = AllowedDirection == TradeBias.Both || AllowedDirection == TradeBias.LongOnly;
            bool canShort = AllowedDirection == TradeBias.Both || AllowedDirection == TradeBias.ShortOnly;

            bool longSignal = canLong
                && PassTrendFilter(true)
                && PassPriceActionFilter(true)
                && PassSharedRsiFilters(true)
                && EvaluateEntrySignal(true);

            bool shortSignal = canShort
                && PassTrendFilter(false)
                && PassPriceActionFilter(false)
                && PassSharedRsiFilters(false)
                && EvaluateEntrySignal(false);

            if (longSignal)
            {
                PrepareProtectiveOrders(LongSignalName);
                lastEntryBar = CurrentBar;
                positionEntryBar = CurrentBar;
                tradesThisSession++;
                EnterLong(OrderQuantity, LongSignalName);
                return;
            }

            if (shortSignal)
            {
                PrepareProtectiveOrders(ShortSignalName);
                lastEntryBar = CurrentBar;
                positionEntryBar = CurrentBar;
                tradesThisSession++;
                EnterShort(OrderQuantity, ShortSignalName);
            }
        }

        private bool ValidateParameters()
        {
            double upperLevel = GetUpperThreshold();

            if (LowerThreshold <= 0 || upperLevel >= 100.0)
                return false;

            if (LowerThreshold >= Midline || Midline >= upperLevel)
                return false;

            if (upperLevel - LowerThreshold < MinimumLevelSeparation)
                return false;

            if ((TrendFilter == TrendFilterMode.FastVsSlowEma || TrendFilter == TrendFilterMode.StackAndSlope)
                && FastTrendPeriod >= SlowTrendPeriod)
                return false;

            if (AdxFilter == AdxFilterMode.Range && MinimumAdx > MaximumAdx)
                return false;

            if (MaximumAtrTicks > 0 && MinimumAtrTicks > MaximumAtrTicks)
                return false;

            return true;
        }

        private int GetRequiredBarsCount()
        {
            int rsiBars = RsiPeriod + RsiSmoothing + SignalLinePeriod + 2;
            int trendBars = SlowTrendPeriod + TrendSlopeLookback + 2;
            int volatilityBars = Math.Max(AtrPeriod, AdxPeriod) + 2;
            int patternBars = Math.Max(ExtremeLookback, SlopeLookback) + 3;
            int timeoutBars = MaxBarsInTrade + 2;
            return Math.Max(30, Math.Max(Math.Max(rsiBars, trendBars), Math.Max(volatilityBars, Math.Max(patternBars, timeoutBars))));
        }

        private void ManageOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                positionEntryBar = -1;
                return;
            }

            bool exitByMidline = false;
            bool exitByOppositeSignal = false;
            bool exitByTimeout = false;

            if (PositionExitMode == ExitMode.Midline
                || PositionExitMode == ExitMode.MidlineOrOpposite
                || PositionExitMode == ExitMode.MidlineOrTimeout)
                exitByMidline = ShouldExitByMidline();

            if (PositionExitMode == ExitMode.OppositeSignal
                || PositionExitMode == ExitMode.MidlineOrOpposite
                || PositionExitMode == ExitMode.OppositeOrTimeout)
                exitByOppositeSignal = ShouldExitByOppositeSignal();

            if (PositionExitMode == ExitMode.TimeoutBars
                || PositionExitMode == ExitMode.MidlineOrTimeout
                || PositionExitMode == ExitMode.OppositeOrTimeout)
                exitByTimeout = ShouldExitByTimeout();

            if (!(exitByMidline || exitByOppositeSignal || exitByTimeout))
                return;

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("ManagedExitLong", LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("ManagedExitShort", ShortSignalName);
        }

        private bool ShouldExitByMidline()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return rsi[0] >= Midline;

            if (Position.MarketPosition == MarketPosition.Short)
                return rsi[0] <= Midline;

            return false;
        }

        private bool ShouldExitByOppositeSignal()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return EvaluateEntrySignal(false);

            if (Position.MarketPosition == MarketPosition.Short)
                return EvaluateEntrySignal(true);

            return false;
        }

        private bool ShouldExitByTimeout()
        {
            if (MaxBarsInTrade <= 0 || positionEntryBar < 0)
                return false;

            return CurrentBar - positionEntryBar >= MaxBarsInTrade;
        }

        private bool EvaluateEntrySignal(bool isLong)
        {
            switch (EntrySignalMode)
            {
                case RsiEntryMode.CrossBackFromExtreme:
                    return isLong
                        ? rsi[1] <= LowerThreshold && rsi[0] > LowerThreshold
                        : rsi[1] >= GetUpperThreshold() && rsi[0] < GetUpperThreshold();

                case RsiEntryMode.ExtremeTouch:
                    return isLong
                        ? rsi[0] <= LowerThreshold
                        : rsi[0] >= GetUpperThreshold();

                case RsiEntryMode.CenterlineCross:
                    return isLong
                        ? rsi[1] <= Midline && rsi[0] > Midline
                        : rsi[1] >= Midline && rsi[0] < Midline;

                case RsiEntryMode.SignalLineCross:
                    return isLong
                        ? rsi[1] <= rsiSignalLine[1] && rsi[0] > rsiSignalLine[0]
                        : rsi[1] >= rsiSignalLine[1] && rsi[0] < rsiSignalLine[0];

                case RsiEntryMode.RecoveryFromExtreme:
                    return isLong
                        ? HasTouchedExtreme(true, ExtremeLookback, 1) && rsi[1] <= Midline && rsi[0] > Midline
                        : HasTouchedExtreme(false, ExtremeLookback, 1) && rsi[1] >= Midline && rsi[0] < Midline;

                case RsiEntryMode.BreakoutThroughExtreme:
                    return isLong
                        ? rsi[1] < GetUpperThreshold() && rsi[0] >= GetUpperThreshold()
                        : rsi[1] > LowerThreshold && rsi[0] <= LowerThreshold;

                case RsiEntryMode.SlopeReversalAfterExtreme:
                    return isLong
                        ? HasTouchedExtreme(true, ExtremeLookback, 1) && GetRsiSlope() >= MinimumSlopePoints && rsi[0] > rsi[1]
                        : HasTouchedExtreme(false, ExtremeLookback, 1) && GetRsiSlope() <= -MinimumSlopePoints && rsi[0] < rsi[1];
            }

            return false;
        }

        private bool PassSharedRsiFilters(bool isLong)
        {
            if (RequireSignalLineAgreement)
            {
                bool signalAgreement = isLong
                    ? rsi[0] >= rsiSignalLine[0]
                    : rsi[0] <= rsiSignalLine[0];

                if (!signalAgreement)
                    return false;
            }

            if (RequireExtremeTouchInLookback && !HasTouchedExtreme(isLong, ExtremeLookback))
                return false;

            return true;
        }

        private bool PassTrendFilter(bool isLong)
        {
            switch (TrendFilter)
            {
                case TrendFilterMode.None:
                    return true;

                case TrendFilterMode.PriceVsSlowEma:
                    return isLong ? Close[0] >= slowEma[0] : Close[0] <= slowEma[0];

                case TrendFilterMode.FastVsSlowEma:
                    return isLong ? fastEma[0] >= slowEma[0] : fastEma[0] <= slowEma[0];

                case TrendFilterMode.SlowEmaSlope:
                    return isLong
                        ? slowEma[0] >= slowEma[TrendSlopeLookback]
                        : slowEma[0] <= slowEma[TrendSlopeLookback];

                case TrendFilterMode.StackAndSlope:
                    if (isLong)
                        return Close[0] >= fastEma[0] && fastEma[0] >= slowEma[0] && slowEma[0] >= slowEma[TrendSlopeLookback];

                    return Close[0] <= fastEma[0] && fastEma[0] <= slowEma[0] && slowEma[0] <= slowEma[TrendSlopeLookback];
            }

            return true;
        }

        private bool PassAdxFilter()
        {
            switch (AdxFilter)
            {
                case AdxFilterMode.None:
                    return true;

                case AdxFilterMode.Minimum:
                    return adx[0] >= MinimumAdx;

                case AdxFilterMode.Maximum:
                    return adx[0] <= MaximumAdx;

                case AdxFilterMode.Range:
                    return adx[0] >= MinimumAdx && adx[0] <= MaximumAdx;
            }

            return true;
        }

        private bool PassAtrFilter()
        {
            double atrTicks = atr[0] / TickSize;

            if (MinimumAtrTicks > 0 && atrTicks < MinimumAtrTicks)
                return false;

            if (MaximumAtrTicks > 0 && atrTicks > MaximumAtrTicks)
                return false;

            return atr[0] > 0;
        }

        private bool PassPriceActionFilter(bool isLong)
        {
            switch (PriceActionFilter)
            {
                case PriceActionFilterMode.None:
                    return true;

                case PriceActionFilterMode.CandleColor:
                    return isLong ? Close[0] >= Open[0] : Close[0] <= Open[0];

                case PriceActionFilterMode.BreakPreviousBar:
                    return isLong ? Close[0] > High[1] : Close[0] < Low[1];

                case PriceActionFilterMode.CandleAndBreak:
                    if (isLong)
                        return Close[0] >= Open[0] && Close[0] > High[1];

                    return Close[0] <= Open[0] && Close[0] < Low[1];
            }

            return true;
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            int stopTicks;
            int targetTicks;

            if (ProtectiveOrderMode == StopTargetMode.Atr)
            {
                stopTicks = Math.Max(1, (int)Math.Round((atr[0] * StopLossAtrMultiplier) / TickSize));
                targetTicks = Math.Max(1, (int)Math.Round((atr[0] * ProfitTargetAtrMultiplier) / TickSize));
            }
            else
            {
                stopTicks = Math.Max(1, StopLossTicks);
                targetTicks = Math.Max(1, ProfitTargetTicks);
            }

            SetStopLoss(signalName, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, targetTicks);
        }

        private void ResetSessionStatsIfNeeded()
        {
            if (!sessionInitialized || Bars.IsFirstBarOfSession)
            {
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                tradesThisSession = 0;
                sessionInitialized = true;
            }
        }

        private double GetSessionPnL()
        {
            double realizedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
            double unrealizedPnL = Position.MarketPosition == MarketPosition.Flat
                ? 0
                : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

            return realizedPnL + unrealizedPnL;
        }

        private bool HasReachedDailyLossLimit()
        {
            if (MaxDailyLoss <= 0)
                return false;

            return GetSessionPnL() <= -Math.Abs(MaxDailyLoss);
        }

        private bool ShouldForceFlat()
        {
            return FlattenAtEndTime
                && Position.MarketPosition != MarketPosition.Flat
                && !IsWithinTradingWindow(ToTime(Time[0]));
        }

        private void ExitOpenPosition(string exitTag)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(exitTag, LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(exitTag, ShortSignalName);
        }

        private bool IsWithinTradingWindow(int currentTime)
        {
            if (StartTime == EndTime)
                return true;

            if (StartTime < EndTime)
                return currentTime >= StartTime && currentTime <= EndTime;

            return currentTime >= StartTime || currentTime <= EndTime;
        }

        private double GetUpperThreshold()
        {
            return UseSymmetricThresholds ? 100.0 - LowerThreshold : UpperThreshold;
        }

        private bool HasTouchedExtreme(bool isLong, int lookback, int startBarsAgo = 0)
        {
            int firstBarsAgo = Math.Max(0, startBarsAgo);
            int maxLookback = Math.Min(CurrentBar, firstBarsAgo + Math.Max(1, lookback) - 1);
            double upperLevel = GetUpperThreshold();

            for (int barsAgo = firstBarsAgo; barsAgo <= maxLookback; barsAgo++)
            {
                if (isLong && rsi[barsAgo] <= LowerThreshold)
                    return true;

                if (!isLong && rsi[barsAgo] >= upperLevel)
                    return true;
            }

            return false;
        }

        private double GetRsiSlope()
        {
            int barsAgo = Math.Min(CurrentBar, Math.Max(1, SlopeLookback));
            return rsi[0] - rsi[barsAgo];
        }

        [NinjaScriptProperty]
        [Display(Name = "Direccion", GroupName = "01. General", Order = 0)]
        public TradeBias AllowedDirection
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Cantidad", GroupName = "01. General", Order = 1)]
        public int OrderQuantity
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora inicio (HHmmss)", GroupName = "01. General", Order = 2)]
        public int StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora fin (HHmmss)", GroupName = "01. General", Order = 3)]
        public int EndTime
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cerrar fuera de horario", GroupName = "01. General", Order = 4)]
        public bool FlattenAtEndTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max trades por sesion", GroupName = "01. General", Order = 5)]
        public int MaxTradesPerSession
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Min barras entre entradas", GroupName = "01. General", Order = 6)]
        public int MinBarsBetweenEntries
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000000.0)]
        [Display(Name = "Max perdida diaria", GroupName = "01. General", Order = 7)]
        public double MaxDailyLoss
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo entrada RSI", GroupName = "02. RSI Core", Order = 0)]
        public RsiEntryMode EntrySignalMode
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "RSI periodo", GroupName = "02. RSI Core", Order = 1)]
        public int RsiPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "RSI smoothing", GroupName = "02. RSI Core", Order = 2)]
        public int RsiSmoothing
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Signal line periodo", GroupName = "02. RSI Core", Order = 3)]
        public int SignalLinePeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 49.0)]
        [Display(Name = "Nivel inferior", GroupName = "02. RSI Core", Order = 4)]
        public double LowerThreshold
        { get; set; }

        [NinjaScriptProperty]
        [Range(51.0, 99.0)]
        [Display(Name = "Nivel superior", GroupName = "02. RSI Core", Order = 5)]
        public double UpperThreshold
        { get; set; }

        [NinjaScriptProperty]
        [Range(30.0, 70.0)]
        [Display(Name = "Linea media", GroupName = "02. RSI Core", Order = 6)]
        public double Midline
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveles simetricos", GroupName = "02. RSI Core", Order = 7)]
        public bool UseSymmetricThresholds
        { get; set; }

        [NinjaScriptProperty]
        [Range(5.0, 80.0)]
        [Display(Name = "Separacion minima niveles", GroupName = "02. RSI Core", Order = 8)]
        public double MinimumLevelSeparation
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Lookback extremo", GroupName = "02. RSI Core", Order = 9)]
        public int ExtremeLookback
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Lookback pendiente", GroupName = "02. RSI Core", Order = 10)]
        public int SlopeLookback
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 50.0)]
        [Display(Name = "Pendiente minima RSI", GroupName = "02. RSI Core", Order = 11)]
        public double MinimumSlopePoints
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir acuerdo con signal line", GroupName = "02. RSI Core", Order = 12)]
        public bool RequireSignalLineAgreement
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir toque extremo reciente", GroupName = "02. RSI Core", Order = 13)]
        public bool RequireExtremeTouchInLookback
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro tendencia", GroupName = "03. Contexto", Order = 0)]
        public TrendFilterMode TrendFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "EMA rapida", GroupName = "03. Contexto", Order = 1)]
        public int FastTrendPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(3, 300)]
        [Display(Name = "EMA lenta", GroupName = "03. Contexto", Order = 2)]
        public int SlowTrendPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Slope lookback EMA", GroupName = "03. Contexto", Order = 3)]
        public int TrendSlopeLookback
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro ADX", GroupName = "03. Contexto", Order = 4)]
        public AdxFilterMode AdxFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ADX periodo", GroupName = "03. Contexto", Order = 5)]
        public int AdxPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX minimo", GroupName = "03. Contexto", Order = 6)]
        public double MinimumAdx
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX maximo", GroupName = "03. Contexto", Order = 7)]
        public double MaximumAdx
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "ATR periodo", GroupName = "03. Contexto", Order = 8)]
        public int AtrPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10000.0)]
        [Display(Name = "ATR minimo en ticks", GroupName = "03. Contexto", Order = 9)]
        public double MinimumAtrTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10000.0)]
        [Display(Name = "ATR maximo en ticks", GroupName = "03. Contexto", Order = 10)]
        public double MaximumAtrTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro accion precio", GroupName = "03. Contexto", Order = 11)]
        public PriceActionFilterMode PriceActionFilter
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo SL/TP", GroupName = "04. Salidas", Order = 0)]
        public StopTargetMode ProtectiveOrderMode
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Stop loss ticks", GroupName = "04. Salidas", Order = 1)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 2000)]
        [Display(Name = "Profit target ticks", GroupName = "04. Salidas", Order = 2)]
        public int ProfitTargetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Stop ATR x", GroupName = "04. Salidas", Order = 3)]
        public double StopLossAtrMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 30.0)]
        [Display(Name = "Target ATR x", GroupName = "04. Salidas", Order = 4)]
        public double ProfitTargetAtrMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo salida", GroupName = "04. Salidas", Order = 5)]
        public ExitMode PositionExitMode
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max barras en trade", GroupName = "04. Salidas", Order = 6)]
        public int MaxBarsInTrade
        { get; set; }
    }
}
