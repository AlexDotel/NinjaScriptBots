#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Windows.Media;
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
    public class BreakoutLookbackStatsBot : Strategy
    {
        private const string LongSignalName = "BLS_LongBreakout";
        private const string ShortSignalName = "BLS_ShortBreakout";

        private int lastProcessedTradeCount;
        private int wins;
        private int losses;
        private int breakevens;
        private int currentWinStreak;
        private int currentLossStreak;
        private int maxWinStreak;
        private int maxLossStreak;
        private int tradeDrawCounter;
        private string lastStatsText;
        private double grossClosedProfit;
        private double simulatedWithdrawals;
        private int withdrawalCount;
        private double cycleProfitAfterReset;
        private double cyclePeakAfterReset;
        private int burnedAccounts;
        private double burnedAccountFees;
        private bool accountPassed;
        private int passedAccounts;
        private double fundedProfitSinceWithdrawal;
        private double currentFundedDayProfit;
        private int currentFundedDayKey;
        private int fundedProfitDays;

        [NinjaScriptProperty]
        [Range(2, 10000)]
        [Display(Name = "Velas breakout hacia atras", GroupName = "01. Breakout", Order = 0)]
        public int LookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir largos", GroupName = "01. Breakout", Order = 1)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir cortos", GroupName = "01. Breakout", Order = 2)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Stop loss (ticks)", GroupName = "02. Riesgo", Order = 0)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Take profit (ticks)", GroupName = "02. Riesgo", Order = 1)]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos", GroupName = "02. Riesgo", Order = 2)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Simular cuentas/retiros", GroupName = "03. Cuentas", Order = 0)]
        public bool SimulateWithdrawals { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Objetivo pasar cuenta ($)", GroupName = "03. Cuentas", Order = 1)]
        public double PassTargetDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "DD cuenta quemada ($)", GroupName = "03. Cuentas", Order = 2)]
        public double BurnDrawdownDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Coste cuenta quemada ($)", GroupName = "03. Cuentas", Order = 3)]
        public double BurnFeeDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Dias para retirar", GroupName = "03. Cuentas", Order = 4)]
        public int FundedDaysRequired { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Minimo dia positivo ($)", GroupName = "03. Cuentas", Order = 5)]
        public double FundedDailyMinProfit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Minimo generado retiro ($)", GroupName = "03. Cuentas", Order = 6)]
        public double WithdrawalProfitMinDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Maximo generado retiro ($)", GroupName = "03. Cuentas", Order = 7)]
        public double WithdrawalProfitCapDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Porcentaje retiro", GroupName = "03. Cuentas", Order = 8)]
        public double WithdrawalPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar ventana horaria", GroupName = "04. Filtro horario", Order = 0)]
        public bool UseTimeWindow { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Inicio (HHmm)", GroupName = "04. Filtro horario", Order = 1)]
        public int StartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Fin (HHmm)", GroupName = "04. Filtro horario", Order = 2)]
        public int EndHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Una operacion a la vez", GroupName = "05. Ejecucion", Order = 0)]
        public bool OneTradeAtATime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entrada al cierre de vela", GroupName = "05. Ejecucion", Order = 1)]
        public bool EnterOnCloseBreakout { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dibujar nivel breakout", GroupName = "06. Visual", Order = 0)]
        public bool DrawBreakoutLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dibujar entrada SL TP", GroupName = "06. Visual", Order = 1)]
        public bool DrawTradeLines { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Largo visual entrada SL TP", GroupName = "06. Visual", Order = 2)]
        public int TradeLineBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar estadisticas", GroupName = "06. Visual", Order = 3)]
        public bool ShowStatsOnChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Imprimir estadisticas al finalizar", GroupName = "07. Debug", Order = 0)]
        public bool PrintStatsOnTerminate { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BreakoutLookbackStatsBot";
                Description = "Detecta breakouts del maximo/minimo de X velas hacia atras, coloca SL/TP y muestra estadisticas basicas en el grafico.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = false;

                LookbackBars = 55;
                AllowLongs = true;
                AllowShorts = true;
                StopLossTicks = 40;
                TakeProfitTicks = 80;
                Quantity = 1;
                SimulateWithdrawals = true;
                PassTargetDollars = 1250;
                BurnDrawdownDollars = 1000;
                BurnFeeDollars = 50;
                FundedDaysRequired = 5;
                FundedDailyMinProfit = 100;
                WithdrawalProfitMinDollars = 500;
                WithdrawalProfitCapDollars = 1000;
                WithdrawalPercent = 50;
                UseTimeWindow = false;
                StartHHmm = 1530;
                EndHHmm = 2200;
                OneTradeAtATime = true;
                EnterOnCloseBreakout = true;
                DrawBreakoutLevels = true;
                DrawTradeLines = false;
                TradeLineBars = 6;
                ShowStatsOnChart = true;
                PrintStatsOnTerminate = false;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = LookbackBars + 2;
                SetStopLoss(LongSignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(LongSignalName, CalculationMode.Ticks, TakeProfitTicks);
                SetStopLoss(ShortSignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(ShortSignalName, CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                ResetStats();
            }
            else if (State == State.Terminated)
            {
                ProcessClosedTrades();
                FinalizeFundedDay();

                if (PrintStatsOnTerminate)
                    Print(BuildStatsText());
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            FinalizeFundedDayIfNeeded(ToDay(Time[0]));
            ProcessClosedTrades();

            if (CurrentBar < LookbackBars + 2)
            {
                RenderStatsIfNeeded();
                return;
            }

            double breakoutHigh = MAX(High, LookbackBars)[1];
            double breakoutLow = MIN(Low, LookbackBars)[1];

            if (DrawBreakoutLevels)
                DrawBreakoutReferenceLines(breakoutHigh, breakoutLow);

            RenderStatsIfNeeded();

            if (OneTradeAtATime && Position.MarketPosition != MarketPosition.Flat)
                return;

            if (UseTimeWindow && !IsInsideTimeWindow(Time[0]))
                return;

            bool longBreakout = AllowLongs && Close[0] > breakoutHigh && Close[1] <= breakoutHigh;
            bool shortBreakout = AllowShorts && Close[0] < breakoutLow && Close[1] >= breakoutLow;

            if (!EnterOnCloseBreakout)
            {
                longBreakout = AllowLongs && High[0] > breakoutHigh && High[1] <= breakoutHigh;
                shortBreakout = AllowShorts && Low[0] < breakoutLow && Low[1] >= breakoutLow;
            }

            if (longBreakout)
                EnterLong(Quantity, LongSignalName);
            else if (shortBreakout)
                EnterShort(Quantity, ShortSignalName);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (!DrawTradeLines || execution == null || execution.Order == null)
                return;

            string signal = execution.Order.Name ?? string.Empty;
            bool isLongEntry = signal == LongSignalName && execution.Order.OrderAction == OrderAction.Buy;
            bool isShortEntry = signal == ShortSignalName && execution.Order.OrderAction == OrderAction.SellShort;

            if (!isLongEntry && !isShortEntry)
                return;

            DrawTradeReferenceLines(price, isLongEntry, time);
        }

        private void ResetStats()
        {
            lastProcessedTradeCount = 0;
            wins = 0;
            losses = 0;
            breakevens = 0;
            currentWinStreak = 0;
            currentLossStreak = 0;
            maxWinStreak = 0;
            maxLossStreak = 0;
            tradeDrawCounter = 0;
            lastStatsText = string.Empty;
            grossClosedProfit = 0;
            simulatedWithdrawals = 0;
            withdrawalCount = 0;
            cycleProfitAfterReset = 0;
            cyclePeakAfterReset = 0;
            burnedAccounts = 0;
            burnedAccountFees = 0;
            accountPassed = false;
            passedAccounts = 0;
            fundedProfitSinceWithdrawal = 0;
            currentFundedDayProfit = 0;
            currentFundedDayKey = -1;
            fundedProfitDays = 0;
        }

        private void ProcessClosedTrades()
        {
            int tradeCount = SystemPerformance.AllTrades.Count;
            if (tradeCount <= lastProcessedTradeCount)
                return;

            for (int i = lastProcessedTradeCount; i < tradeCount; i++)
                RegisterClosedTrade(SystemPerformance.AllTrades[i].ProfitCurrency, ToDay(Time[0]));

            lastProcessedTradeCount = tradeCount;
        }

        private void RegisterClosedTrade(double profitCurrency, int tradeDayKey)
        {
            grossClosedProfit += profitCurrency;
            UpdateAccountCycle(profitCurrency, tradeDayKey);

            if (profitCurrency > 0)
            {
                wins++;
                currentWinStreak++;
                currentLossStreak = 0;
                maxWinStreak = Math.Max(maxWinStreak, currentWinStreak);
                return;
            }

            if (profitCurrency < 0)
            {
                losses++;
                currentLossStreak++;
                currentWinStreak = 0;
                maxLossStreak = Math.Max(maxLossStreak, currentLossStreak);
                return;
            }

            breakevens++;
            currentWinStreak = 0;
            currentLossStreak = 0;
        }

        private void UpdateAccountCycle(double profitCurrency, int tradeDayKey)
        {
            if (!SimulateWithdrawals)
                return;

            if (accountPassed)
                AddFundedProfit(profitCurrency, tradeDayKey);

            cycleProfitAfterReset += profitCurrency;
            cyclePeakAfterReset = Math.Max(cyclePeakAfterReset, cycleProfitAfterReset);

            if (!accountPassed && PassTargetDollars > 0 && cycleProfitAfterReset >= PassTargetDollars)
            {
                passedAccounts++;
                accountPassed = true;
                ResetCurrentCycle();
                ResetFundedWithdrawalCycle();
                return;
            }

            if (BurnDrawdownDollars <= 0)
                return;

            double cycleDrawdown = cyclePeakAfterReset - cycleProfitAfterReset;
            if (cycleDrawdown >= BurnDrawdownDollars)
                BurnCurrentAccount();
        }

        private void AddFundedProfit(double profitCurrency, int tradeDayKey)
        {
            if (currentFundedDayKey < 0)
                currentFundedDayKey = tradeDayKey;
            else if (currentFundedDayKey != tradeDayKey)
            {
                FinalizeFundedDay();
                currentFundedDayKey = tradeDayKey;
            }

            currentFundedDayProfit += profitCurrency;
            fundedProfitSinceWithdrawal += profitCurrency;
        }

        private void FinalizeFundedDay()
        {
            if (currentFundedDayKey < 0)
                return;

            if (currentFundedDayProfit >= FundedDailyMinProfit)
                fundedProfitDays++;

            currentFundedDayProfit = 0;
            TryFundedWithdrawal();
        }

        private void FinalizeFundedDayIfNeeded(int dayKey)
        {
            if (!accountPassed || currentFundedDayKey < 0 || currentFundedDayKey == dayKey)
                return;

            FinalizeFundedDay();
            currentFundedDayKey = -1;
        }

        private void TryFundedWithdrawal()
        {
            if (fundedProfitDays < FundedDaysRequired)
                return;

            if (fundedProfitSinceWithdrawal < WithdrawalProfitMinDollars)
                return;

            double cappedGeneratedProfit = Math.Min(fundedProfitSinceWithdrawal, WithdrawalProfitCapDollars);
            double withdrawal = cappedGeneratedProfit * (WithdrawalPercent / 100.0);

            if (withdrawal <= 0)
                return;

            withdrawalCount++;
            simulatedWithdrawals += withdrawal;
            ResetCurrentCycle();
            ResetFundedWithdrawalCycle();
        }

        private void BurnCurrentAccount()
        {
            burnedAccounts++;
            burnedAccountFees += BurnFeeDollars;
            accountPassed = false;
            ResetCurrentCycle();
            ResetFundedWithdrawalCycle();
        }

        private void ResetCurrentCycle()
        {
            cycleProfitAfterReset = 0;
            cyclePeakAfterReset = 0;
        }

        private void ResetFundedWithdrawalCycle()
        {
            fundedProfitSinceWithdrawal = 0;
            currentFundedDayProfit = 0;
            currentFundedDayKey = -1;
            fundedProfitDays = 0;
        }

        private void DrawBreakoutReferenceLines(double breakoutHigh, double breakoutLow)
        {
            Draw.HorizontalLine(this, "BLS_CurrentHigh", breakoutHigh, Brushes.DodgerBlue);
            Draw.HorizontalLine(this, "BLS_CurrentLow", breakoutLow, Brushes.OrangeRed);
        }

        private void DrawTradeReferenceLines(double entryPrice, bool isLong, DateTime time)
        {
            tradeDrawCounter++;

            double target = entryPrice + (isLong ? TakeProfitTicks : -TakeProfitTicks) * TickSize;
            double stop = entryPrice + (isLong ? -StopLossTicks : StopLossTicks) * TickSize;
            string baseTag = string.Format("BLS_TRADE_{0:yyyyMMdd_HHmmss}_{1}", time, tradeDrawCounter);
            int startBarsAgo = Math.Min(TradeLineBars, CurrentBar);

            Draw.Line(this, baseTag + "_ENTRY", false, startBarsAgo, entryPrice, 0, entryPrice, Brushes.Gold, DashStyleHelper.Solid, 2);
            Draw.Line(this, baseTag + "_TP", false, startBarsAgo, target, 0, target, Brushes.LimeGreen, DashStyleHelper.Solid, 2);
            Draw.Line(this, baseTag + "_SL", false, startBarsAgo, stop, 0, stop, Brushes.Red, DashStyleHelper.Solid, 2);
            Draw.VerticalLine(this, baseTag + "_BAR", 0, Brushes.DimGray);
        }

        private void RenderStatsIfNeeded()
        {
            if (!ShowStatsOnChart)
                return;

            string statsText = BuildStatsText();
            if (statsText == lastStatsText)
                return;

            Draw.TextFixed(
                this,
                "BLS_Stats",
                statsText,
                TextPosition.TopLeft,
                Brushes.White,
                new SimpleFont("Consolas", 12),
                Brushes.Black,
                Brushes.Black,
                70);

            lastStatsText = statsText;
        }

        private string BuildStatsText()
        {
            int totalTrades = wins + losses + breakevens;
            int resolvedTrades = wins + losses;
            double winRate = resolvedTrades > 0 ? 100.0 * wins / resolvedTrades : 0.0;
            double takeProfitRate = totalTrades > 0 ? 100.0 * wins / totalTrades : 0.0;
            double simulatedBalance = grossClosedProfit - simulatedWithdrawals - burnedAccountFees;
            double netCashExtracted = simulatedWithdrawals - burnedAccountFees;
            double currentCycleDrawdown = cyclePeakAfterReset - cycleProfitAfterReset;
            double displayedFundedDayProfit = currentFundedDayKey >= 0 ? currentFundedDayProfit : 0;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("BREAKOUT LOOKBACK STATS");
            builder.AppendLine(string.Format("Lookback: {0} velas | SL {1}t | TP {2}t", LookbackBars, StopLossTicks, TakeProfitTicks));
            builder.AppendLine(string.Format("Trades: {0} | TP: {1} | SL: {2} | BE: {3}", totalTrades, wins, losses, breakevens));
            builder.AppendLine(string.Format("Acierto resuelto: {0:N1}% | TP total: {1:N1}%", winRate, takeProfitRate));
            builder.AppendLine(string.Format("PnL bruto: ${0:N2} | Saldo tras retiros/costes: ${1:N2}", grossClosedProfit, simulatedBalance));
            builder.AppendLine(string.Format("Pasadas: {0} | Estado: {1} | Ciclo: ${2:N2} | DD: ${3:N2}", passedAccounts, accountPassed ? "Financiada" : "Evaluacion", cycleProfitAfterReset, currentCycleDrawdown));
            builder.AppendLine(string.Format("Dias retiro: {0}/{1} | Dia actual: ${2:N2} | Generado: ${3:N2}", fundedProfitDays, FundedDaysRequired, displayedFundedDayProfit, fundedProfitSinceWithdrawal));
            builder.AppendLine(string.Format("Retiros: {0} (${1:N2}) | Quemadas: {2} (-${3:N2}) | Neto: ${4:N2}", withdrawalCount, simulatedWithdrawals, burnedAccounts, burnedAccountFees, netCashExtracted));
            builder.Append(string.Format("Racha ganadora max: {0} | Racha perdedora max: {1}", maxWinStreak, maxLossStreak));
            return builder.ToString();
        }

        private bool IsInsideTimeWindow(DateTime barTime)
        {
            int now = ToTime(barTime);
            int start = HHmmToIntTime(StartHHmm);
            int end = HHmmToIntTime(EndHHmm);

            if (start <= end)
                return now >= start && now <= end;

            return now >= start || now <= end;
        }

        private int HHmmToIntTime(int hhmm)
        {
            int hours = Math.Max(0, Math.Min(23, hhmm / 100));
            int minutes = Math.Max(0, Math.Min(59, hhmm % 100));
            return hours * 10000 + minutes * 100;
        }
    }
}
