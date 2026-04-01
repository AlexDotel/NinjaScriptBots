#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SP500MeanReversionBot : Strategy
    {
        public enum TradeBias
        {
            Both,
            LongOnly,
            ShortOnly
        }

        private const string LongSignalName = "MRLong";
        private const string ShortSignalName = "MRShort";

        private Bollinger bollinger;
        private SMA mean;
        private RSI rsi;
        private ATR atr;
        private ADX adx;

        private double sessionStartCumProfit;
        private bool sessionInitialized;
        private int tradesThisSession;
        private int lastEntryBar;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Bot de reversion a la media para ES/MES usando Bandas de Bollinger, RSI y filtro ADX.";
                Name = "SP500MeanReversionBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 30;
                DefaultQuantity = 1;
                Slippage = 1;

                TradeDirection = TradeBias.Both;
                StartTime = 93000;
                EndTime = 160000;
                FlattenAtEndTime = true;

                BollingerPeriod = 20;
                BollingerDeviation = 2.0;
                RsiPeriod = 5;
                RsiSmoothing = 3;
                OversoldLevel = 30;
                OverboughtLevel = 70;
                AdxPeriod = 14;
                MaxAdx = 25;

                AtrPeriod = 14;
                StopLossAtrMultiplier = 1.5;
                ProfitTargetAtrMultiplier = 1.8;
                MaxDailyLoss = 400;
                MaxTradesPerSession = 6;
                MinBarsBetweenEntries = 3;

                lastEntryBar = -1;
            }
            else if (State == State.DataLoaded)
            {
                bollinger = Bollinger(BollingerDeviation, BollingerPeriod);
                mean = SMA(BollingerPeriod);
                rsi = RSI(RsiPeriod, RsiSmoothing);
                atr = ATR(AtrPeriod);
                adx = ADX(AdxPeriod);

                AddChartIndicator(bollinger);
                AddChartIndicator(mean);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
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

            if (atr[0] <= 0)
                return;

            if (MaxAdx > 0 && adx[0] > MaxAdx)
                return;

            bool canLong = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.LongOnly;
            bool canShort = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.ShortOnly;

            bool longSignal = canLong
                && Close[1] < bollinger.Lower[1]
                && Close[0] > bollinger.Lower[0]
                && rsi[0] <= OversoldLevel;

            bool shortSignal = canShort
                && Close[1] > bollinger.Upper[1]
                && Close[0] < bollinger.Upper[0]
                && rsi[0] >= OverboughtLevel;

            if (longSignal)
            {
                PrepareProtectiveOrders(LongSignalName);
                lastEntryBar = CurrentBar;
                tradesThisSession++;
                EnterLong(LongSignalName);
                return;
            }

            if (shortSignal)
            {
                PrepareProtectiveOrders(ShortSignalName);
                lastEntryBar = CurrentBar;
                tradesThisSession++;
                EnterShort(ShortSignalName);
            }
        }

        private void ManageOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Long && Close[0] >= mean[0])
                ExitLong("MeanExitLong", LongSignalName);

            if (Position.MarketPosition == MarketPosition.Short && Close[0] <= mean[0])
                ExitShort("MeanExitShort", ShortSignalName);
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            int stopTicks = Math.Max(1, (int) Math.Round((atr[0] * StopLossAtrMultiplier) / TickSize));
            int targetTicks = Math.Max(1, (int) Math.Round((atr[0] * ProfitTargetAtrMultiplier) / TickSize));

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

            // Permite ventanas que crucen medianoche, por ejemplo 220000 -> 020000.
            return currentTime >= StartTime || currentTime <= EndTime;
        }

        [NinjaScriptProperty]
        [Display(Name = "Direccion", GroupName = "01. Filtros", Order = 0)]
        public TradeBias TradeDirection
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora inicio (HHmmss)", GroupName = "01. Filtros", Order = 1)]
        public int StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora fin (HHmmss)", GroupName = "01. Filtros", Order = 2)]
        public int EndTime
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cerrar fuera de horario", GroupName = "01. Filtros", Order = 3)]
        public bool FlattenAtEndTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Periodo Bollinger", GroupName = "02. Senal", Order = 0)]
        public int BollingerPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 5.0)]
        [Display(Name = "Desviacion Bollinger", GroupName = "02. Senal", Order = 1)]
        public double BollingerDeviation
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "RSI periodo", GroupName = "02. Senal", Order = 2)]
        public int RsiPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "RSI suavizado", GroupName = "02. Senal", Order = 3)]
        public int RsiSmoothing
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "RSI sobreventa", GroupName = "02. Senal", Order = 4)]
        public int OversoldLevel
        { get; set; }

        [NinjaScriptProperty]
        [Range(50, 99)]
        [Display(Name = "RSI sobrecompra", GroupName = "02. Senal", Order = 5)]
        public int OverboughtLevel
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "ADX periodo", GroupName = "02. Senal", Order = 6)]
        public int AdxPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX maximo", GroupName = "02. Senal", Order = 7)]
        public int MaxAdx
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "ATR periodo", GroupName = "03. Riesgo", Order = 0)]
        public int AtrPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Stop x ATR", GroupName = "03. Riesgo", Order = 1)]
        public double StopLossAtrMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Target x ATR", GroupName = "03. Riesgo", Order = 2)]
        public double ProfitTargetAtrMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100000.0)]
        [Display(Name = "Perdida diaria max", GroupName = "03. Riesgo", Order = 3)]
        public double MaxDailyLoss
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max trades por sesion", GroupName = "03. Riesgo", Order = 4)]
        public int MaxTradesPerSession
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Min barras entre entradas", GroupName = "03. Riesgo", Order = 5)]
        public int MinBarsBetweenEntries
        { get; set; }
    }
}
