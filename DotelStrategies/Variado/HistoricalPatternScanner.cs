#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Windows.Media;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class HistoricalPatternScanner : Strategy
    {
        private enum OutcomeResult
        {
            Win,
            Loss,
            Unresolved
        }

        private sealed class PatternStats
        {
            public PatternStats(string patternKey)
            {
                PatternKey = patternKey;
            }

            public string PatternKey { get; private set; }
            public int Occurrences { get; set; }
            public int LongWins { get; set; }
            public int LongLosses { get; set; }
            public int LongUnresolved { get; set; }
            public int ShortWins { get; set; }
            public int ShortLosses { get; set; }
            public int ShortUnresolved { get; set; }
        }

        private sealed class PatternCandidate
        {
            public string PatternKey { get; set; }
            public string DirectionLabel { get; set; }
            public int Occurrences { get; set; }
            public int Resolved { get; set; }
            public int Wins { get; set; }
            public int Losses { get; set; }
            public double WinRatePercent { get; set; }
            public double ResolutionRatePercent { get; set; }
        }

        private ATR atr;
        private EMA ema;
        private Dictionary<string, PatternStats> statsByPattern;
        private string lastRenderedSummary;
        private bool reportPrinted;

        [NinjaScriptProperty]
        [Range(1, 6)]
        [Display(Name = "Barras del patron", GroupName = "01. Patron", Order = 0)]
        public int PatternLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Velas hacia delante", GroupName = "02. Resultado", Order = 0)]
        public int OutcomeBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Target ticks", GroupName = "02. Resultado", Order = 1)]
        public int TargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Stop ticks", GroupName = "02. Resultado", Order = 2)]
        public int StopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Misma barra TP/SL = perdida", GroupName = "02. Resultado", Order = 3)]
        public bool CountSameBarConflictAsLoss { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "EMA contexto", GroupName = "03. Contexto", Order = 0)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ATR contexto", GroupName = "03. Contexto", Order = 1)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Min ocurrencias", GroupName = "04. Filtro estadistico", Order = 0)]
        public int MinOccurrences { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Min casos resueltos", GroupName = "04. Filtro estadistico", Order = 1)]
        public int MinResolvedCases { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min win rate %", GroupName = "04. Filtro estadistico", Order = 2)]
        public double MinWinRatePercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max patrones visibles", GroupName = "05. Salida", Order = 0)]
        public int MaxPatternsToShow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar resumen en chart", GroupName = "05. Salida", Order = 1)]
        public bool ShowSummaryOnChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Imprimir reporte en Output", GroupName = "05. Salida", Order = 2)]
        public bool PrintReportToOutput { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "HistoricalPatternScanner";
                Description = "Escaner estadistico de patrones sobre el historico cargado en el chart. No abre operaciones; detecta patrones repetitivos y mide si alcanzan un target antes de un stop.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = false;
                IncludeCommission = false;

                PatternLength = 3;
                OutcomeBars = 8;
                TargetTicks = 12;
                StopTicks = 12;
                CountSameBarConflictAsLoss = true;

                EmaPeriod = 50;
                AtrPeriod = 14;

                MinOccurrences = 25;
                MinResolvedCases = 15;
                MinWinRatePercent = 50.0;

                MaxPatternsToShow = 6;
                ShowSummaryOnChart = true;
                PrintReportToOutput = true;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = GetRequiredBarsCount();
            }
            else if (State == State.DataLoaded)
            {
                ema = EMA(EmaPeriod);
                atr = ATR(AtrPeriod);
                statsByPattern = new Dictionary<string, PatternStats>();
                lastRenderedSummary = string.Empty;
                reportPrinted = false;
            }
            else if (State == State.Realtime)
            {
                PrintReportIfNeeded();
            }
            else if (State == State.Terminated)
            {
                PrintReportIfNeeded();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < GetRequiredBarsCount() - 1)
                return;

            AnalyzeResolvedPattern();
            RenderSummaryIfNeeded();
        }

        private int GetRequiredBarsCount()
        {
            int contextBars = Math.Max(EmaPeriod, AtrPeriod);
            int patternBars = PatternLength + OutcomeBars + 2;
            return Math.Max(contextBars, patternBars);
        }

        private void AnalyzeResolvedPattern()
        {
            // Evaluamos el patron que termino OutcomeBars velas atras, porque ya conocemos su desenlace.
            int patternEndBarsAgo = OutcomeBars;
            string patternKey = BuildPatternKey(patternEndBarsAgo);

            PatternStats stats;
            if (!statsByPattern.TryGetValue(patternKey, out stats))
            {
                stats = new PatternStats(patternKey);
                statsByPattern[patternKey] = stats;
            }

            stats.Occurrences++;

            OutcomeResult longOutcome = EvaluateOutcome(patternEndBarsAgo, true);
            OutcomeResult shortOutcome = EvaluateOutcome(patternEndBarsAgo, false);

            RegisterOutcome(stats, longOutcome, true);
            RegisterOutcome(stats, shortOutcome, false);
        }

        private void RegisterOutcome(PatternStats stats, OutcomeResult outcome, bool isLong)
        {
            if (isLong)
            {
                if (outcome == OutcomeResult.Win)
                    stats.LongWins++;
                else if (outcome == OutcomeResult.Loss)
                    stats.LongLosses++;
                else
                    stats.LongUnresolved++;

                return;
            }

            if (outcome == OutcomeResult.Win)
                stats.ShortWins++;
            else if (outcome == OutcomeResult.Loss)
                stats.ShortLosses++;
            else
                stats.ShortUnresolved++;
        }

        private OutcomeResult EvaluateOutcome(int patternEndBarsAgo, bool isLong)
        {
            int entryBarsAgo = patternEndBarsAgo - 1;
            double entryPrice = Open[entryBarsAgo];
            double targetPrice = isLong
                ? entryPrice + (TargetTicks * TickSize)
                : entryPrice - (TargetTicks * TickSize);
            double stopPrice = isLong
                ? entryPrice - (StopTicks * TickSize)
                : entryPrice + (StopTicks * TickSize);

            for (int barsAgo = entryBarsAgo; barsAgo >= 0; barsAgo--)
            {
                bool hitTarget = isLong
                    ? High[barsAgo] >= targetPrice
                    : Low[barsAgo] <= targetPrice;
                bool hitStop = isLong
                    ? Low[barsAgo] <= stopPrice
                    : High[barsAgo] >= stopPrice;

                // Con OHLC no conocemos el orden intrabar; por defecto lo tratamos como resultado adverso.
                if (hitTarget && hitStop)
                    return CountSameBarConflictAsLoss ? OutcomeResult.Loss : OutcomeResult.Unresolved;

                if (hitTarget)
                    return OutcomeResult.Win;

                if (hitStop)
                    return OutcomeResult.Loss;
            }

            return OutcomeResult.Unresolved;
        }

        private string BuildPatternKey(int patternEndBarsAgo)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Seq=");

            for (int i = PatternLength - 1; i >= 0; i--)
            {
                int barsAgo = patternEndBarsAgo + i;

                if (i != PatternLength - 1)
                    builder.Append(">");

                builder.Append(GetBarToken(barsAgo));
            }

            builder.Append(" | Struct=");
            builder.Append(GetStructureLabel(patternEndBarsAgo));
            builder.Append(" | Trend=");
            builder.Append(GetTrendLabel(patternEndBarsAgo));
            builder.Append(" | Range=");
            builder.Append(GetRangeLabel(patternEndBarsAgo));

            return builder.ToString();
        }

        private string GetBarToken(int barsAgo)
        {
            char directionCode;
            if (Close[barsAgo] > Open[barsAgo])
                directionCode = 'U';
            else if (Close[barsAgo] < Open[barsAgo])
                directionCode = 'D';
            else
                directionCode = 'N';

            double range = Math.Max(TickSize, High[barsAgo] - Low[barsAgo]);
            double bodyFraction = Math.Abs(Close[barsAgo] - Open[barsAgo]) / range;

            char bodyCode;
            if (bodyFraction < 0.33)
                bodyCode = 'S';
            else if (bodyFraction < 0.66)
                bodyCode = 'M';
            else
                bodyCode = 'L';

            return string.Format("{0}{1}", directionCode, bodyCode);
        }

        private string GetStructureLabel(int barsAgo)
        {
            if (High[barsAgo] <= High[barsAgo + 1] && Low[barsAgo] >= Low[barsAgo + 1])
                return "Inside";

            if (High[barsAgo] >= High[barsAgo + 1] && Low[barsAgo] <= Low[barsAgo + 1])
                return "Outside";

            return "Regular";
        }

        private string GetTrendLabel(int barsAgo)
        {
            return Close[barsAgo] >= ema[barsAgo] ? "AboveEma" : "BelowEma";
        }

        private string GetRangeLabel(int barsAgo)
        {
            double atrValue = atr[barsAgo];
            if (atrValue <= TickSize)
                return "Unknown";

            double range = High[barsAgo] - Low[barsAgo];
            if (range < atrValue * 0.8)
                return "Small";

            if (range > atrValue * 1.2)
                return "Large";

            return "Normal";
        }

        private List<PatternCandidate> BuildRankedCandidates()
        {
            List<PatternCandidate> candidates = new List<PatternCandidate>();

            foreach (KeyValuePair<string, PatternStats> pair in statsByPattern)
            {
                AddCandidateIfValid(candidates, pair.Value, true);
                AddCandidateIfValid(candidates, pair.Value, false);
            }

            candidates.Sort(CompareCandidates);
            return candidates;
        }

        private void AddCandidateIfValid(List<PatternCandidate> candidates, PatternStats stats, bool isLong)
        {
            int wins = isLong ? stats.LongWins : stats.ShortWins;
            int losses = isLong ? stats.LongLosses : stats.ShortLosses;
            int unresolved = isLong ? stats.LongUnresolved : stats.ShortUnresolved;
            int resolved = wins + losses;

            if (stats.Occurrences < MinOccurrences)
                return;

            if (resolved < MinResolvedCases)
                return;

            double winRatePercent = stats.Occurrences <= 0
                ? 0.0
                : (100.0 * wins / stats.Occurrences);

            if (winRatePercent < MinWinRatePercent)
                return;

            double resolutionRatePercent = stats.Occurrences <= 0
                ? 0.0
                : (100.0 * resolved / stats.Occurrences);

            candidates.Add(new PatternCandidate
            {
                PatternKey = stats.PatternKey,
                DirectionLabel = isLong ? "LARGO" : "CORTO",
                Occurrences = stats.Occurrences,
                Resolved = resolved,
                Wins = wins,
                Losses = losses,
                WinRatePercent = winRatePercent,
                ResolutionRatePercent = resolutionRatePercent
            });
        }

        private int CompareCandidates(PatternCandidate x, PatternCandidate y)
        {
            int compare = y.WinRatePercent.CompareTo(x.WinRatePercent);
            if (compare != 0)
                return compare;

            compare = y.Wins.CompareTo(x.Wins);
            if (compare != 0)
                return compare;

            compare = y.Occurrences.CompareTo(x.Occurrences);
            if (compare != 0)
                return compare;

            compare = y.Resolved.CompareTo(x.Resolved);
            if (compare != 0)
                return compare;

            return string.Compare(x.PatternKey, y.PatternKey, StringComparison.Ordinal);
        }

        private void RenderSummaryIfNeeded()
        {
            if (!ShowSummaryOnChart)
                return;

            string summary = BuildSummaryText(Math.Min(MaxPatternsToShow, 6));
            if (summary == lastRenderedSummary)
                return;

            Draw.TextFixed(
                this,
                "HistoricalPatternScannerSummary",
                summary,
                TextPosition.TopLeft,
                Brushes.LightSteelBlue,
                new SimpleFont("Consolas", 12),
                Brushes.Transparent,
                Brushes.Transparent,
                0);

            lastRenderedSummary = summary;
        }

        private string BuildSummaryText(int maxItems)
        {
            List<PatternCandidate> candidates = BuildRankedCandidates();
            StringBuilder builder = new StringBuilder();

            builder.AppendLine("HISTORICAL PATTERN SCANNER");
            builder.AppendLine(string.Format(
                "{0} | {1} barras | X = TP {2}t antes de SL {3}t",
                Instrument != null ? Instrument.FullName : "Instrumento",
                OutcomeBars,
                TargetTicks,
                StopTicks));
            builder.AppendLine(string.Format(
                "Min occ {0} | Min res {1} | Win rate >= {2:N1}%",
                MinOccurrences,
                MinResolvedCases,
                MinWinRatePercent));
            builder.AppendLine(string.Format("Barras analizadas: {0}", CurrentBar + 1));
            builder.AppendLine();

            if (candidates.Count == 0)
            {
                builder.AppendLine("No hay patrones que superen el filtro.");
                builder.AppendLine();
                builder.Append("Clave: U/D/N = direccion, S/M/L = tamano del cuerpo.");
                return builder.ToString().TrimEnd();
            }

            int visibleCount = Math.Min(maxItems, candidates.Count);
            for (int i = 0; i < visibleCount; i++)
            {
                PatternCandidate candidate = candidates[i];
                builder.AppendLine(string.Format(
                    "{0}. {1} {2:N1}% | wins {3}/{4} | res {5}/{4} | {6}",
                    i + 1,
                    candidate.DirectionLabel,
                    candidate.WinRatePercent,
                    candidate.Wins,
                    candidate.Occurrences,
                    candidate.Resolved,
                    candidate.PatternKey));
            }

            builder.AppendLine();
            builder.Append("Clave: U/D/N = direccion, S/M/L = tamano del cuerpo.");
            return builder.ToString().TrimEnd();
        }

        private void PrintReportIfNeeded()
        {
            if (!PrintReportToOutput || reportPrinted || statsByPattern == null)
                return;

            Print("========== HistoricalPatternScanner ==========");
            Print(BuildSummaryText(MaxPatternsToShow));
            Print("=============================================");
            reportPrinted = true;
        }
    }
}
