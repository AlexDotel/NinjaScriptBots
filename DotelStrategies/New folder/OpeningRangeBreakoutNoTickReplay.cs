#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public enum OrbQuantityMode { Fixed, RiskAdjusted }
    public enum OrbStopMode { Range0, Fib236, Fib382, Fib500, Fib618, Fib764, Range100, Points, BreakoutCandle, FvgExtreme, SuperTrend }
    public enum OrbTargetMode { Points, RiskReward, FibonacciExtension }
    public enum OrbBreakEvenMode { Points, TargetPercentage }
    public enum OrbSuperTrendStopBehavior { RestingStopAtLine, StopAtCandleExtremeAfterClose }

    /// <summary>
    /// Clean-room implementation of the publicly documented ATCH Opening Range
    /// Breakout rules. It can route orders through an internal one-tick series
    /// for granular historical fills without requiring platform Tick Replay.
    /// </summary>
    public class OpeningRangeBreakoutNoTickReplay : Strategy
    {
        private const string LongEntry = "ORB-L";
        private const string ShortEntry = "ORB-S";
        private static readonly DateTime ExpirationDate = new DateTime(2026, 8, 30);

        private double rangeHigh, rangeLow;
        private bool rangeStarted, rangeReady;
        private int rangeStartBar;
        private int longState, shortState; // 0=waiting break, 1=waiting retest, 2=waiting trigger
        private int longRetestBar = -1, shortRetestBar = -1;
        private int fixedBias;             // 0=unset, 1=long, -1=short
        private DateTime logicalDate;
        private double pendingStop, pendingTarget, entryPrice;
        private bool breakEvenDone, scaledOut;
        private double sessionStartCumProfit, weekStartCumProfit;
        private int sessionWins, sessionLosses;
        private int countedTrades;
        private ATR superTrendAtr;
        private HMA superTrendAtrHma;
        private Series<double> superTrendUpper, superTrendLower, superTrendLine;
        private TimeZoneInfo easternTimeZone;
        private Chart chartWindow;
        private ChartTrader chartTrader;
        private Grid chartTraderGrid;
        private Border statusPanelBorder;
        private StackPanel statusPanelStack;
        private TextBlock tbBotName, tbStatus, tbExpiry, tbDailyPnl, tbPosition, tbNewYorkTime;
        private string panelStatusText = "INICIANDO";
        private string panelPnlText = "$0.00";
        private string panelPositionText = "Sin posicion";
        private string panelTimeText = "--";
        private bool tickEntryPending;
        private bool pendingEntryIsLong;
        private int pendingEntryQuantity;
        private bool superTrendCloseStopPlaced;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "OpeningRangeBreakoutNoTickReplay";
                Description = "ORB con ruptura, retesteo, FVG y ejecución histórica granular mediante serie interna de 1 tick.";
                AddPlot(Brushes.Goldenrod, "SuperTrend");
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 5;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                ConnectionLossHandling = ConnectionLossHandling.KeepRunning;

                RangeStart = DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
                RangeEnd = DateTime.Parse("09:45", System.Globalization.CultureInfo.InvariantCulture);
                TradeStart = DateTime.Parse("09:45", System.Globalization.CultureInfo.InvariantCulture);
                TradeEnd = DateTime.Parse("11:00", System.Globalization.CultureInfo.InvariantCulture);
                RangeOffsetTicks = 0;
                WaitForRetest = true;
                WaitForFvg = true;
                RequireFvgLastCandleColor = true;
                ResetBiasOnEachSide = false;
                AllowLongs = true;
                AllowShorts = true;
                MinDistancePoints = 0;
                MaxDistancePoints = 0;
                MinRangePoints = 0;
                MaxRangePoints = 0;

                QuantityMode = OrbQuantityMode.RiskAdjusted;
                FixedQuantity = 1;
                MaxQuantity = 50;
                TargetRiskCurrency = 600;
                MaxAllowedRiskCurrency = 620;
                StopMode = OrbStopMode.SuperTrend;
                StopPoints = 10;
                StopOffsetPoints = 0;
                TargetMode = OrbTargetMode.RiskReward;
                TargetPoints = 20;
                RiskReward = 2;
                TargetFibonacciExtension = 0.618;
                EnableSuperTrendEntryFilter = true;
                SuperTrendLength = 42;
                SuperTrendMultiplier = 2.5;
                SuperTrendSmooth = 42;
                ManageWithSuperTrend = true;
                SuperTrendStopBehavior = OrbSuperTrendStopBehavior.StopAtCandleExtremeAfterClose;
                DrawSuperTrend = true;
                UseOneTickExecutionSeries = true;

                EnableBreakEven = false;
                BreakEvenMode = OrbBreakEvenMode.Points;
                BreakEvenTrigger = 8;
                EnableScaleOut = false;
                ScaleOutAtPercent = 50;
                ScaleOutQuantityPercent = 50;
                DailyProfitLimit = 0;
                DailyLossLimit = 0;
                WeeklyProfitLimit = 0;
                StopAfterWins = 0;
                StopAfterLosses = 0;
                TradeMonday = TradeTuesday = TradeWednesday = TradeThursday = TradeFriday = true;
                FlattenAtTradeEnd = false;
                DrawRange = true;
                RangeOpacity = 10;
                ShowStatusPanel = true;
                ShowCompactChartStatus = true;
            }
            else if (State == State.Configure)
            {
                // Serie granular interna siguiendo la práctica recomendada para
                // fills intrabar históricos. No depende de la casilla Tick Replay.
                if (UseOneTickExecutionSeries)
                    AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                logicalDate = DateTime.MinValue;
                countedTrades = SystemPerformance.AllTrades.Count;
                superTrendAtr = ATR(SuperTrendLength);
                superTrendAtrHma = HMA(superTrendAtr, SuperTrendSmooth);
                superTrendUpper = new Series<double>(this);
                superTrendLower = new Series<double>(this);
                superTrendLine = new Series<double>(this);
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            else if (State == State.Historical)
            {
                TryCreateStatusPanel();
            }
            else if (State == State.Terminated)
            {
                RemoveStatusPanel();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                SubmitPendingEntryOnTickSeries();
                return;
            }

            if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
                return;

            UpdateSuperTrend();

            // NinjaTrader timestamps bars in the platform-configured timezone.
            // Convert explicitly so all configured hours always mean New York time.
            DateTime easternBarTime = TimeZoneInfo.ConvertTime(
                Time[0], NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo, easternTimeZone);
            DateTime d = easternBarTime.Date;

            if (d != logicalDate)
                ResetDay(d);
            CountNewClosedTrades();

            if (ShowCompactChartStatus)
                DrawCompactChartStatus(easternBarTime);
            else
                RemoveDrawObject("ORB-CompactStatus");

            if (ShowStatusPanel)
                DrawStatusPanel(easternBarTime);
            else if (statusPanelBorder != null)
                RemoveStatusPanel();

            // Caducidad fija: el 30/08/2026 está incluido; desde el
            // 31/08/2026 la estrategia no puede mantener ni abrir posiciones.
            if (d > ExpirationDate)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    Flatten("StrategyExpired");
                return;
            }

            int now = ToTime(easternBarTime);
            int rs = ToTime(RangeStart);
            int re = ToTime(RangeEnd);
            int ts = ToTime(TradeStart);
            int te = ToTime(TradeEnd);

            // En barras de tiempo, NinjaTrader usa la hora de cierre como sello.
            // La barra marcada 09:45 representa el último minuto del ORB y debe
            // incluirse antes de cerrar el rango.
            bool isRangeBar = InClockWindow(now, rs, re) || now == re;
            if (isRangeBar)
            {
                double hi = High[0] + RangeOffsetTicks * TickSize;
                double lo = Low[0] - RangeOffsetTicks * TickSize;
                if (!rangeStarted)
                {
                    rangeHigh = hi;
                    rangeLow = lo;
                    rangeStarted = true;
                    rangeStartBar = CurrentBar;
                }
                else { rangeHigh = Math.Max(rangeHigh, hi); rangeLow = Math.Min(rangeLow, lo); }

                // Antes de la barra final seguimos acumulando. En la barra con
                // sello 09:45 continuamos para finalizar y dibujar el rango.
                if (now != re)
                    return;
            }

            if (rangeStarted && !rangeReady && PassedEnd(now, re))
            {
                rangeReady = rangeHigh > rangeLow;
            }

            // Grow today's two range segments only through its trading window.
            // Unlike HorizontalLine, these drawings do not contaminate every
            // subsequent day on a historical chart.
            if (rangeReady && DrawRange && (InClockWindow(now, ts, te) || now == te))
                DrawCurrentRange(d);

            ManageOpenPosition();
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (FlattenAtTradeEnd && !InClockWindow(now, ts, te) && PassedEnd(now, te))
                    Flatten("TradeWindowEnd");
                return;
            }

            if (!rangeReady || !InClockWindow(now, ts, te) || !AllowedWeekday(d.DayOfWeek) || DailyLimitsHit())
                return;

            double height = rangeHigh - rangeLow;
            if ((MinRangePoints > 0 && height < MinRangePoints) || (MaxRangePoints > 0 && height > MaxRangePoints))
                return;

            UpdateEntryState();
            if (AllowLongs && LongTrigger()) TryEnter(true);
            else if (AllowShorts && ShortTrigger()) TryEnter(false);
        }

        private void UpdateEntryState()
        {
            bool above = Close[0] > rangeHigh;
            bool below = Close[0] < rangeLow;
            bool longBreakEvent = above && Close[1] <= rangeHigh;
            bool shortBreakEvent = below && Close[1] >= rangeLow;
            bool bullishTrend = SuperTrendAllows(true);
            bool bearishTrend = SuperTrendAllows(false);

            // La dirección de SuperTrend debe permanecer válida desde la rotura
            // hasta el FVG. Un cambio de color invalida toda la secuencia.
            if (EnableSuperTrendEntryFilter)
            {
                if (!bullishTrend) { longState = 0; longRetestBar = -1; }
                if (!bearishTrend) { shortState = 0; shortRetestBar = -1; }
            }

            bool longDirectionAllowed = ResetBiasOnEachSide || fixedBias >= 0;
            bool shortDirectionAllowed = ResetBiasOnEachSide || fixedBias <= 0;
            bool validLongBreak = longBreakEvent && longDirectionAllowed
                && (!EnableSuperTrendEntryFilter || bullishTrend);
            bool validShortBreak = shortBreakEvent && shortDirectionAllowed
                && (!EnableSuperTrendEntryFilter || bearishTrend);

            if (!WaitForRetest)
            {
                if (validLongBreak)
                {
                    if (fixedBias == 0) fixedBias = 1;
                    longState = 2;
                    longRetestBar = CurrentBar;
                }
                if (validShortBreak)
                {
                    if (fixedBias == 0) fixedBias = -1;
                    shortState = 2;
                    shortRetestBar = CurrentBar;
                }
                return;
            }

            // El retesteo puede ocurrir en la propia vela de ruptura. Para una
            // venta basta High >= rangeLow y cierre bajo rangeLow; para compra,
            // Low <= rangeHigh y cierre sobre rangeHigh.
            if (longState == 0 && validLongBreak)
            {
                if (fixedBias == 0) fixedBias = 1;
                longState = Low[0] <= rangeHigh ? 2 : 1;
                if (longState == 2) longRetestBar = CurrentBar;
            }
            else if (longState == 1 && Low[0] <= rangeHigh)
            {
                longState = 2;
                longRetestBar = CurrentBar;
            }

            if (shortState == 0 && validShortBreak)
            {
                if (fixedBias == 0) fixedBias = -1;
                shortState = High[0] >= rangeLow ? 2 : 1;
                if (shortState == 2) shortRetestBar = CurrentBar;
            }
            else if (shortState == 1 && High[0] >= rangeLow)
            {
                shortState = 2;
                shortRetestBar = CurrentBar;
            }
        }

        private bool LongTrigger()
        {
            if (longState != 2 || longRetestBar < 0 || CurrentBar - 2 < longRetestBar
                || Close[0] <= rangeHigh || !DistanceOk(Close[0] - rangeHigh)) return false;
            if (!WaitForFvg) return Close[0] > Open[0];
            bool threeBullishCandles = Close[0] > Open[0] && Close[1] > Open[1] && Close[2] > Open[2];
            return Low[0] > High[2] && (!RequireFvgLastCandleColor || threeBullishCandles);
        }

        private bool ShortTrigger()
        {
            if (shortState != 2 || shortRetestBar < 0 || CurrentBar - 2 < shortRetestBar
                || Close[0] >= rangeLow || !DistanceOk(rangeLow - Close[0])) return false;
            if (!WaitForFvg) return Close[0] < Open[0];
            bool threeBearishCandles = Close[0] < Open[0] && Close[1] < Open[1] && Close[2] < Open[2];
            return High[0] < Low[2] && (!RequireFvgLastCandleColor || threeBearishCandles);
        }

        private bool DistanceOk(double distance)
        {
            return distance >= MinDistancePoints && (MaxDistancePoints <= 0 || distance <= MaxDistancePoints);
        }

        private void TryEnter(bool isLong)
        {
            if (EnableSuperTrendEntryFilter && !SuperTrendAllows(isLong))
            {
                // Template mode TriggerThenFilter: the completed setup is consumed
                // even when SuperTrend rejects the resulting entry.
                if (isLong) { longState = 0; longRetestBar = -1; }
                else { shortState = 0; shortRetestBar = -1; }
                return;
            }
            double assumedEntry = Close[0];
            double stop = ComputeStop(isLong, assumedEntry);
            if ((isLong && stop >= assumedEntry) || (!isLong && stop <= assumedEntry)) return;

            double riskPerContract = Math.Abs(assumedEntry - stop) * Instrument.MasterInstrument.PointValue;
            if (riskPerContract <= 0) return;
            int qty = QuantityMode == OrbQuantityMode.Fixed
                ? FixedQuantity
                : Math.Max(1, Math.Min(MaxQuantity, (int)Math.Floor(TargetRiskCurrency / riskPerContract)));
            double actualRisk = qty * riskPerContract;
            if (TargetRiskCurrency > 0 && QuantityMode == OrbQuantityMode.Fixed && actualRisk > TargetRiskCurrency) return;
            if (MaxAllowedRiskCurrency > 0 && actualRisk > MaxAllowedRiskCurrency) return;

            double target = ComputeTarget(isLong, assumedEntry, stop);
            pendingStop = Instrument.MasterInstrument.RoundToTickSize(stop);
            pendingTarget = Instrument.MasterInstrument.RoundToTickSize(target);
            bool deferSuperTrendStop = StopMode == OrbStopMode.SuperTrend
                && SuperTrendStopBehavior == OrbSuperTrendStopBehavior.StopAtCandleExtremeAfterClose;
            if (!deferSuperTrendStop)
                SetStopLoss(isLong ? LongEntry : ShortEntry, CalculationMode.Price, pendingStop, false);
            SetProfitTarget(isLong ? LongEntry : ShortEntry, CalculationMode.Price, pendingTarget);
            if (UseOneTickExecutionSeries)
            {
                tickEntryPending = true;
                pendingEntryIsLong = isLong;
                pendingEntryQuantity = qty;
            }
            else if (isLong) EnterLong(qty, LongEntry);
            else EnterShort(qty, ShortEntry);
            entryPrice = assumedEntry;
            breakEvenDone = scaledOut = false;
            superTrendCloseStopPlaced = false;
            if (isLong) { longState = 0; longRetestBar = -1; }
            else { shortState = 0; shortRetestBar = -1; }
        }

        private void SubmitPendingEntryOnTickSeries()
        {
            if (!UseOneTickExecutionSeries || !tickEntryPending || CurrentBars[1] < 0)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                tickEntryPending = false;
                return;
            }

            if (pendingEntryIsLong)
                EnterLong(1, pendingEntryQuantity, LongEntry);
            else
                EnterShort(1, pendingEntryQuantity, ShortEntry);
            tickEntryPending = false;
        }

        private double ComputeStop(bool isLong, double entry)
        {
            double h = rangeHigh - rangeLow;
            double fraction;
            switch (StopMode)
            {
                case OrbStopMode.Range0: fraction = 0; break;
                case OrbStopMode.Fib236: fraction = .236; break;
                case OrbStopMode.Fib382: fraction = .382; break;
                case OrbStopMode.Fib500: fraction = .5; break;
                case OrbStopMode.Fib618: fraction = .618; break;
                case OrbStopMode.Fib764: fraction = .764; break;
                case OrbStopMode.Range100: fraction = 1; break;
                case OrbStopMode.Points: return isLong ? entry - StopPoints : entry + StopPoints;
                case OrbStopMode.BreakoutCandle: return isLong ? Low[0] - StopOffsetPoints : High[0] + StopOffsetPoints;
                case OrbStopMode.FvgExtreme: return isLong ? Low[2] - StopOffsetPoints : High[2] + StopOffsetPoints;
                case OrbStopMode.SuperTrend: return superTrendLine[0] + (isLong ? -StopOffsetPoints : StopOffsetPoints);
                default: fraction = .618; break;
            }
            return isLong ? rangeHigh - h * fraction - StopOffsetPoints : rangeLow + h * fraction + StopOffsetPoints;
        }

        private double ComputeTarget(bool isLong, double entry, double stop)
        {
            double distance;
            if (TargetMode == OrbTargetMode.Points) distance = TargetPoints;
            else if (TargetMode == OrbTargetMode.FibonacciExtension) distance = (rangeHigh - rangeLow) * TargetFibonacciExtension;
            else distance = Math.Abs(entry - stop) * RiskReward;
            return isLong ? entry + distance : entry - distance;
        }

        private void ManageOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double progress = isLong ? Close[0] - entryPrice : entryPrice - Close[0];
            double fullTarget = Math.Abs(pendingTarget - entryPrice);

            if (ManageWithSuperTrend)
            {
                if (SuperTrendStopBehavior == OrbSuperTrendStopBehavior.RestingStopAtLine)
                {
                    double candidate = Instrument.MasterInstrument.RoundToTickSize(superTrendLine[0]);
                    bool improves = isLong ? candidate > pendingStop && candidate < Close[0] : candidate < pendingStop && candidate > Close[0];
                    if (improves)
                    {
                        pendingStop = candidate;
                        SetStopLoss(isLong ? LongEntry : ShortEntry, CalculationMode.Price, pendingStop, false);
                    }
                }
                else if (!superTrendCloseStopPlaced)
                {
                    bool closedBeyondTrend = isLong ? Close[0] < superTrendLine[0] : Close[0] > superTrendLine[0];
                    if (closedBeyondTrend)
                    {
                        double candleExtreme = isLong
                            ? Low[0] - StopOffsetPoints
                            : High[0] + StopOffsetPoints;
                        pendingStop = Instrument.MasterInstrument.RoundToTickSize(candleExtreme);
                        SetStopLoss(isLong ? LongEntry : ShortEntry, CalculationMode.Price, pendingStop, false);
                        superTrendCloseStopPlaced = true;
                    }
                }
            }

            double breakEvenThreshold = BreakEvenMode == OrbBreakEvenMode.Points
                ? BreakEvenTrigger : fullTarget * BreakEvenTrigger / 100.0;
            if (EnableBreakEven && !breakEvenDone && progress >= breakEvenThreshold)
            {
                SetStopLoss(isLong ? LongEntry : ShortEntry, CalculationMode.Price,
                    Instrument.MasterInstrument.RoundToTickSize(entryPrice), false);
                breakEvenDone = true;
            }
            if (EnableScaleOut && !scaledOut && fullTarget > 0 && progress >= fullTarget * ScaleOutAtPercent / 100.0)
            {
                int q = Math.Min(Position.Quantity - 1, (int)Math.Floor(Position.Quantity * ScaleOutQuantityPercent / 100.0));
                if (q > 0)
                {
                    int executionSeries = UseOneTickExecutionSeries ? 1 : 0;
                    if (isLong) ExitLong(executionSeries, q, "ORB-ScaleOut", LongEntry);
                    else ExitShort(executionSeries, q, "ORB-ScaleOut", ShortEntry);
                    scaledOut = true;
                }
            }
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (position.Instrument != Instrument) return;
            if (marketPosition != MarketPosition.Flat) entryPrice = averagePrice;
        }

        private void CountNewClosedTrades()
        {
            while (countedTrades < SystemPerformance.AllTrades.Count)
            {
                double pnl = SystemPerformance.AllTrades[countedTrades].ProfitCurrency;
                if (pnl > 0) sessionWins++; else if (pnl < 0) sessionLosses++;
                countedTrades++;
            }
        }

        private bool DailyLimitsHit()
        {
            double cum = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            double day = cum - sessionStartCumProfit;
            double week = cum - weekStartCumProfit;
            return (DailyProfitLimit > 0 && day >= DailyProfitLimit)
                || (DailyLossLimit > 0 && day <= -DailyLossLimit)
                || (WeeklyProfitLimit > 0 && week >= WeeklyProfitLimit)
                || (StopAfterWins > 0 && sessionWins >= StopAfterWins)
                || (StopAfterLosses > 0 && sessionLosses >= StopAfterLosses);
        }

        private void UpdateSuperTrend()
        {
            double volatility = Math.Max(TickSize, superTrendAtrHma[0]);
            double middle = (High[0] + Low[0]) * 0.5;
            double basicUpper = middle + SuperTrendMultiplier * volatility;
            double basicLower = middle - SuperTrendMultiplier * volatility;

            if (CurrentBar == 0 || superTrendUpper[1] == 0)
            {
                superTrendUpper[0] = basicUpper;
                superTrendLower[0] = basicLower;
                superTrendLine[0] = Close[0] >= middle ? basicLower : basicUpper;
                Values[0][0] = DrawSuperTrend ? superTrendLine[0] : double.NaN;
                if (DrawSuperTrend) PlotBrushes[0][0] = Close[0] >= superTrendLine[0] ? Brushes.LimeGreen : Brushes.Red;
                return;
            }

            superTrendUpper[0] = basicUpper < superTrendUpper[1] || Close[1] > superTrendUpper[1]
                ? basicUpper : superTrendUpper[1];
            superTrendLower[0] = basicLower > superTrendLower[1] || Close[1] < superTrendLower[1]
                ? basicLower : superTrendLower[1];

            bool previousWasUpper = Math.Abs(superTrendLine[1] - superTrendUpper[1]) < TickSize * 0.5;
            superTrendLine[0] = previousWasUpper
                ? (Close[0] <= superTrendUpper[0] ? superTrendUpper[0] : superTrendLower[0])
                : (Close[0] >= superTrendLower[0] ? superTrendLower[0] : superTrendUpper[0]);
            Values[0][0] = DrawSuperTrend ? superTrendLine[0] : double.NaN;
            if (DrawSuperTrend) PlotBrushes[0][0] = Close[0] >= superTrendLine[0] ? Brushes.LimeGreen : Brushes.Red;
        }

        private bool SuperTrendAllows(bool isLong)
        {
            return isLong ? Close[0] > superTrendLine[0] : Close[0] < superTrendLine[0];
        }

        private void ResetDay(DateTime date)
        {
            double cum = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            if (logicalDate == DateTime.MinValue || date.DayOfWeek == DayOfWeek.Monday) weekStartCumProfit = cum;
            logicalDate = date;
            rangeHigh = double.MinValue; rangeLow = double.MaxValue;
            rangeStarted = rangeReady = false;
            rangeStartBar = -1;
            longState = shortState = fixedBias = 0;
            longRetestBar = shortRetestBar = -1;
            tickEntryPending = false;
            sessionStartCumProfit = cum; sessionWins = sessionLosses = 0;
        }

        private void Flatten(string reason)
        {
            int executionSeries = UseOneTickExecutionSeries ? 1 : 0;
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(executionSeries, Position.Quantity, reason, LongEntry);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(executionSeries, Position.Quantity, reason, ShortEntry);
        }

        private void DrawCurrentRange(DateTime date)
        {
            if (rangeStartBar < 0 || CurrentBar < rangeStartBar) return;
            int startBarsAgo = CurrentBar - rangeStartBar;
            string suffix = date.ToString("yyyyMMdd");
            Draw.Line(this, "ORH-" + suffix, false, startBarsAgo, rangeHigh, 0, rangeHigh,
                Brushes.DodgerBlue, DashStyleHelper.Solid, 1);
            Draw.Line(this, "ORL-" + suffix, false, startBarsAgo, rangeLow, 0, rangeLow,
                Brushes.DodgerBlue, DashStyleHelper.Solid, 1);
            Draw.Rectangle(this, "ORZ-" + suffix, false, startBarsAgo, rangeHigh, 0, rangeLow,
                Brushes.DodgerBlue, Brushes.DodgerBlue, RangeOpacity);
        }

        private void DrawStatusPanel(DateTime easternTime)
        {
            double cumulativeProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            double dailyRealizedProfit = cumulativeProfit - sessionStartCumProfit;
            bool expired = easternTime.Date > ExpirationDate;
            bool limitsReached = !expired && DailyLimitsHit();
            string status = expired ? "CADUCADO / NO FUNCIONA"
                : limitsReached ? "PAUSADO POR LIMITE DIARIO"
                : "ACTIVO / FUNCIONANDO";
            string positionText = Position.MarketPosition == MarketPosition.Flat
                ? "Sin posicion"
                : Position.MarketPosition == MarketPosition.Long
                    ? "Largo x" + Position.Quantity
                    : "Corto x" + Position.Quantity;
            panelStatusText = status;
            panelPnlText = dailyRealizedProfit.ToString("+$#,##0.00;-$#,##0.00;$0.00");
            panelPositionText = positionText;
            panelTimeText = easternTime.ToString("dd/MM/yyyy HH:mm");
            TryCreateStatusPanel();
            RefreshStatusPanel();
        }

        private void DrawCompactChartStatus(DateTime easternTime)
        {
            bool running = easternTime.Date <= ExpirationDate && !DailyLimitsHit();
            string text = running ? "ORB FUNCIONANDO" : "ORB NO FUNCIONANDO";
            Brush color = running ? Brushes.LimeGreen : Brushes.Red;
            Draw.TextFixed(this, "ORB-CompactStatus", text, TextPosition.BottomLeft, color,
                new SimpleFont("Aptos", 13), Brushes.Transparent, Brushes.Transparent, 0);
        }

        private void TryCreateStatusPanel()
        {
            if (!ShowStatusPanel || ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (statusPanelBorder != null)
                        return;

                    chartWindow = Window.GetWindow(ChartControl.Parent) as Chart;
                    if (chartWindow == null) return;
                    chartTrader = chartWindow.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                    if (chartTrader == null) return;
                    chartTraderGrid = chartTrader.FindName("grdMain") as Grid;
                    if (chartTraderGrid == null) return;

                    statusPanelStack = new StackPanel { Orientation = Orientation.Vertical };
                    tbBotName = MakePanelText("OPENING RANGE BREAKOUT", FontWeights.Bold, 14);
                    tbStatus = MakePanelText("", FontWeights.Bold, 13);
                    tbExpiry = MakePanelText("", FontWeights.Normal, 13);
                    tbDailyPnl = MakePanelText("", FontWeights.Bold, 13);
                    tbPosition = MakePanelText("", FontWeights.Normal, 13);
                    tbNewYorkTime = MakePanelText("", FontWeights.Normal, 13);
                    statusPanelStack.Children.Add(tbBotName);
                    statusPanelStack.Children.Add(MakePanelSeparator());
                    statusPanelStack.Children.Add(tbStatus);
                    statusPanelStack.Children.Add(tbExpiry);
                    statusPanelStack.Children.Add(tbDailyPnl);
                    statusPanelStack.Children.Add(tbPosition);
                    statusPanelStack.Children.Add(tbNewYorkTime);

                    statusPanelBorder = new Border
                    {
                        Name = "DotelOpeningRangeBreakoutStatusPanel",
                        Background = Brushes.Black,
                        BorderBrush = Brushes.DimGray,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(14, 12, 14, 12),
                        Margin = new Thickness(6),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Child = statusPanelStack
                    };

                    int targetRow = chartTraderGrid.RowDefinitions.Count;
                    chartTraderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(statusPanelBorder, targetRow);
                    Grid.SetColumn(statusPanelBorder, 0);
                    System.Windows.Controls.Panel.SetZIndex(statusPanelBorder, 99999);
                    chartTraderGrid.Children.Add(statusPanelBorder);
                    RemoveDrawObject("ORB-StatusPanel");
                    RefreshStatusPanel();
                }
                catch (Exception ex) { Print("Error creando panel ORB en Chart Trader: " + ex.Message); }
            });
        }

        private void RefreshStatusPanel()
        {
            if (ChartControl == null || statusPanelBorder == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (statusPanelBorder == null) return;
                    tbStatus.Text = "Estado: " + panelStatusText;
                    tbExpiry.Text = "Fecha limite: 30/08/2026";
                    tbDailyPnl.Text = "PnL realizado hoy: " + panelPnlText;
                    tbPosition.Text = "Posicion: " + panelPositionText;
                    tbNewYorkTime.Text = "Hora NY: " + panelTimeText;
                }
                catch (Exception ex) { Print("Error actualizando panel ORB: " + ex.Message); }
            });
        }

        private TextBlock MakePanelText(string text, FontWeight weight, double size)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = weight,
                FontSize = size,
                FontFamily = new FontFamily("Aptos"),
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private Border MakePanelSeparator()
        {
            return new Border { Height = 1, Margin = new Thickness(0, 7, 0, 7), Background = Brushes.DimGray };
        }

        private void RemoveStatusPanel()
        {
            if (ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (chartTraderGrid != null && statusPanelBorder != null && chartTraderGrid.Children.Contains(statusPanelBorder))
                        chartTraderGrid.Children.Remove(statusPanelBorder);
                }
                catch (Exception ex) { Print("Error retirando panel ORB: " + ex.Message); }
                finally
                {
                    statusPanelBorder = null; statusPanelStack = null;
                    chartWindow = null; chartTrader = null; chartTraderGrid = null;
                    tbBotName = null; tbStatus = null; tbExpiry = null; tbDailyPnl = null;
                    tbPosition = null; tbNewYorkTime = null;
                }
            });
        }

        private static bool InClockWindow(int now, int start, int end) { return start <= end ? now >= start && now < end : now >= start || now < end; }
        private static bool PassedEnd(int now, int end) { return now >= end; }
        private bool AllowedWeekday(DayOfWeek d)
        {
            return (d == DayOfWeek.Monday && TradeMonday) || (d == DayOfWeek.Tuesday && TradeTuesday)
                || (d == DayOfWeek.Wednesday && TradeWednesday) || (d == DayOfWeek.Thursday && TradeThursday)
                || (d == DayOfWeek.Friday && TradeFriday);
        }

        #region Properties
        [NinjaScriptProperty, Display(Name="Ejecutar mediante serie interna de 1 tick", Description="Envía entradas y salidas manuales por una serie auxiliar de un tick para mayor granularidad histórica.", GroupName="00. Ejecución", Order=0)] public bool UseOneTickExecutionSeries { get; set; }
        [NinjaScriptProperty, PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey"), Display(Name="Inicio rango (EST/EDT)", GroupName="01. Horario Nueva York", Order=0)] public DateTime RangeStart { get; set; }
        [NinjaScriptProperty, PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey"), Display(Name="Fin rango (EST/EDT)", GroupName="01. Horario Nueva York", Order=1)] public DateTime RangeEnd { get; set; }
        [NinjaScriptProperty, PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey"), Display(Name="Inicio trading (EST/EDT)", GroupName="01. Horario Nueva York", Order=2)] public DateTime TradeStart { get; set; }
        [NinjaScriptProperty, PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey"), Display(Name="Fin trading (EST/EDT)", GroupName="01. Horario Nueva York", Order=3)] public DateTime TradeEnd { get; set; }
        [NinjaScriptProperty, Range(0,1000), Display(Name="Offset rango (ticks)", GroupName="02. Entrada", Order=0)] public int RangeOffsetTicks { get; set; }
        [NinjaScriptProperty, Display(Name="Esperar retesteo", GroupName="02. Entrada", Order=1)] public bool WaitForRetest { get; set; }
        [NinjaScriptProperty, Display(Name="Esperar FVG", GroupName="02. Entrada", Order=2)] public bool WaitForFvg { get; set; }
        [NinjaScriptProperty, Display(Name="Exigir color en las 3 velas FVG", Description="Para compras exige tres velas alcistas; para ventas exige tres velas bajistas.", GroupName="02. Entrada", Order=3)] public bool RequireFvgLastCandleColor { get; set; }
        [NinjaScriptProperty, Display(Name="Resetear sesgo por lado", GroupName="02. Entrada", Order=4)] public bool ResetBiasOnEachSide { get; set; }
        [NinjaScriptProperty, Display(Name="Permitir largos", GroupName="02. Entrada", Order=5)] public bool AllowLongs { get; set; }
        [NinjaScriptProperty, Display(Name="Permitir cortos", GroupName="02. Entrada", Order=6)] public bool AllowShorts { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Distancia mínima (puntos)", GroupName="02. Entrada", Order=7)] public double MinDistancePoints { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Distancia máxima (puntos)", GroupName="02. Entrada", Order=8)] public double MaxDistancePoints { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Altura mínima rango", GroupName="02. Entrada", Order=9)] public double MinRangePoints { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Altura máxima rango", GroupName="02. Entrada", Order=10)] public double MaxRangePoints { get; set; }
        [NinjaScriptProperty, Display(Name="Modo cantidad", GroupName="03. Riesgo", Order=0)] public OrbQuantityMode QuantityMode { get; set; }
        [NinjaScriptProperty, Range(1,1000), Display(Name="Cantidad fija", GroupName="03. Riesgo", Order=1)] public int FixedQuantity { get; set; }
        [NinjaScriptProperty, Range(1,1000), Display(Name="Cantidad máxima", GroupName="03. Riesgo", Order=2)] public int MaxQuantity { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Riesgo objetivo ($)", GroupName="03. Riesgo", Order=3)] public double TargetRiskCurrency { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Riesgo máximo ($)", GroupName="03. Riesgo", Order=4)] public double MaxAllowedRiskCurrency { get; set; }
        [NinjaScriptProperty, Display(Name="Tipo stop", GroupName="04. Salidas", Order=0)] public OrbStopMode StopMode { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Stop (puntos)", GroupName="04. Salidas", Order=1)] public double StopPoints { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Offset stop (puntos)", GroupName="04. Salidas", Order=2)] public double StopOffsetPoints { get; set; }
        [NinjaScriptProperty, Display(Name="Tipo objetivo", GroupName="04. Salidas", Order=3)] public OrbTargetMode TargetMode { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Objetivo (puntos)", GroupName="04. Salidas", Order=4)] public double TargetPoints { get; set; }
        [NinjaScriptProperty, Range(0.01,100), Display(Name="Ratio beneficio/riesgo", GroupName="04. Salidas", Order=5)] public double RiskReward { get; set; }
        [NinjaScriptProperty, Range(0.01,100), Display(Name="Extensión Fibonacci", GroupName="04. Salidas", Order=6)] public double TargetFibonacciExtension { get; set; }
        [NinjaScriptProperty, Display(Name="Filtro entrada SuperTrend", GroupName="04. SuperTrend", Order=0)] public bool EnableSuperTrendEntryFilter { get; set; }
        [NinjaScriptProperty, Range(1,1000), Display(Name="Periodo ATR", GroupName="04. SuperTrend", Order=1)] public int SuperTrendLength { get; set; }
        [NinjaScriptProperty, Range(0.01,100), Display(Name="Multiplicador", GroupName="04. SuperTrend", Order=2)] public double SuperTrendMultiplier { get; set; }
        [NinjaScriptProperty, Range(1,1000), Display(Name="Suavizado HMA", GroupName="04. SuperTrend", Order=3)] public int SuperTrendSmooth { get; set; }
        [NinjaScriptProperty, Display(Name="Gestionar con SuperTrend", GroupName="04. SuperTrend", Order=4)] public bool ManageWithSuperTrend { get; set; }
        [NinjaScriptProperty, Display(Name="Comportamiento stop SuperTrend", Description="Stop permanente sobre la línea o stop en el extremo de la vela después de cerrar al otro lado del SuperTrend.", GroupName="04. SuperTrend", Order=5)] public OrbSuperTrendStopBehavior SuperTrendStopBehavior { get; set; }
        [NinjaScriptProperty, Display(Name="Dibujar SuperTrend", Description="Desactivar para acelerar la visualización de backtests; el cálculo y la operativa continúan activos.", GroupName="04. SuperTrend", Order=6)] public bool DrawSuperTrend { get; set; }
        [NinjaScriptProperty, Display(Name="Activar breakeven", GroupName="05. Gestión", Order=0)] public bool EnableBreakEven { get; set; }
        [NinjaScriptProperty, Display(Name="Modo breakeven", GroupName="05. Gestión", Order=1)] public OrbBreakEvenMode BreakEvenMode { get; set; }
        [NinjaScriptProperty, Range(0.01,double.MaxValue), Display(Name="Trigger breakeven", GroupName="05. Gestión", Order=2)] public double BreakEvenTrigger { get; set; }
        [NinjaScriptProperty, Display(Name="Activar salida parcial", GroupName="05. Gestión", Order=3)] public bool EnableScaleOut { get; set; }
        [NinjaScriptProperty, Range(1,100), Display(Name="Trigger parcial (% TP)", GroupName="05. Gestión", Order=3)] public double ScaleOutAtPercent { get; set; }
        [NinjaScriptProperty, Range(1,99), Display(Name="Cantidad parcial (%)", GroupName="05. Gestión", Order=4)] public double ScaleOutQuantityPercent { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Objetivo diario ($)", GroupName="06. Límites", Order=0)] public double DailyProfitLimit { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Pérdida diaria ($)", GroupName="06. Límites", Order=1)] public double DailyLossLimit { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Objetivo semanal ($)", GroupName="06. Límites", Order=2)] public double WeeklyProfitLimit { get; set; }
        [NinjaScriptProperty, Range(0,1000), Display(Name="Parar tras ganadoras", GroupName="06. Límites", Order=3)] public int StopAfterWins { get; set; }
        [NinjaScriptProperty, Range(0,1000), Display(Name="Parar tras perdedoras", GroupName="06. Límites", Order=4)] public int StopAfterLosses { get; set; }
        [NinjaScriptProperty, Display(Name="Lunes", GroupName="07. Días", Order=0)] public bool TradeMonday { get; set; }
        [NinjaScriptProperty, Display(Name="Martes", GroupName="07. Días", Order=1)] public bool TradeTuesday { get; set; }
        [NinjaScriptProperty, Display(Name="Miércoles", GroupName="07. Días", Order=2)] public bool TradeWednesday { get; set; }
        [NinjaScriptProperty, Display(Name="Jueves", GroupName="07. Días", Order=3)] public bool TradeThursday { get; set; }
        [NinjaScriptProperty, Display(Name="Viernes", GroupName="07. Días", Order=4)] public bool TradeFriday { get; set; }
        [NinjaScriptProperty, Display(Name="Cerrar al fin ventana", GroupName="08. Visual", Order=0)] public bool FlattenAtTradeEnd { get; set; }
        [NinjaScriptProperty, Display(Name="Dibujar rango", GroupName="08. Visual", Order=1)] public bool DrawRange { get; set; }
        [NinjaScriptProperty, Range(1,100), Display(Name="Opacidad zona", GroupName="08. Visual", Order=2)] public int RangeOpacity { get; set; }
        [NinjaScriptProperty, Display(Name="Mostrar panel de estado", GroupName="08. Visual", Order=3)] public bool ShowStatusPanel { get; set; }
        [NinjaScriptProperty, Display(Name="Mostrar estado pequeño en gráfico", GroupName="08. Visual", Order=4)] public bool ShowCompactChartStatus { get; set; }
        #endregion
    }
}
