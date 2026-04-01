#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class Breakout55WithFilters_RiskDollars : Strategy
    {
        // ===== Indicadores =====
        private ATR atr;
        private ADX adx;
        private EMA trendEma;

        // ===== Control diario =====
        private int lastSessionDate = -1;
        private bool tradedToday = false;

        #region Inputs

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "Breakout Lookback (N)", Order = 1, GroupName = "1. Breakout")]
        public int Lookback { get; set; } = 55;

        [NinjaScriptProperty]
        [Display(Name = "Allow Long", Order = 2, GroupName = "1. Breakout")]
        public bool AllowLong { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Allow Short", Order = 3, GroupName = "1. Breakout")]
        public bool AllowShort { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "One trade per day", Order = 4, GroupName = "1. Breakout")]
        public bool OneTradePerDay { get; set; } = true;

        // ===== Risk: ATR SL + RRR TP + Risk in dollars (auto qty) =====
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ATR Period", Order = 10, GroupName = "2. Risk")]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0.1, 50)]
        [Display(Name = "ATR Multiplier (SL)", Order = 11, GroupName = "2. Risk")]
        public double AtrMultSL { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Range(0.1, 50)]
        [Display(Name = "RRR (TP = SL * RRR)", Order = 12, GroupName = "2. Risk")]
        public double Rrr { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Risk Dollars ($)", Order = 13, GroupName = "2. Risk")]
        public double RiskDollars { get; set; } = 200;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Max Contracts", Order = 14, GroupName = "2. Risk")]
        public int MaxContracts { get; set; } = 5;

        // ===== Time filters =====
        [NinjaScriptProperty]
        [Display(Name = "Use Time Window", Order = 20, GroupName = "3. Time Filters")]
        public bool UseTimeWindow { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Start (HHmm)", Order = 21, GroupName = "3. Time Filters")]
        public int StartHHmm { get; set; } = 1530;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "End (HHmm)", Order = 22, GroupName = "3. Time Filters")]
        public int EndHHmm { get; set; } = 1730;

        [NinjaScriptProperty]
        [Display(Name = "Use Forced Close Time", Order = 23, GroupName = "3. Time Filters")]
        public bool UseCloseTime { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Close (HHmm)", Order = 24, GroupName = "3. Time Filters")]
        public int CloseHHmm { get; set; } = 2200;

        // ===== Trend filter (EMA) =====
        [NinjaScriptProperty]
        [Display(Name = "Use Trend EMA Filter", Order = 30, GroupName = "4. Optional Filters")]
        public bool UseTrendEma { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "Trend EMA Period (0 = off)", Order = 31, GroupName = "4. Optional Filters")]
        public int TrendEmaPeriod { get; set; } = 200;

        // ===== ADX filter =====
        [NinjaScriptProperty]
        [Display(Name = "Use ADX Filter", Order = 40, GroupName = "4. Optional Filters")]
        public bool UseAdxFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Order = 41, GroupName = "4. Optional Filters")]
        public int AdxPeriod { get; set; } = 14;

        // “Como siempre me pides”: ADX <= AdxMax
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Max (Require ADX <= Max)", Order = 42, GroupName = "4. Optional Filters")]
        public double AdxMax { get; set; } = 25;

        // ===== Break-even =====
        [NinjaScriptProperty]
        [Display(Name = "Use Break Even", Order = 50, GroupName = "5. Trade Management")]
        public bool UseBreakEven { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "BE Trigger (ticks in profit)", Order = 51, GroupName = "5. Trade Management")]
        public int BeTriggerTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "BE Offset (ticks)", Order = 52, GroupName = "5. Trade Management")]
        public int BeOffsetTicks { get; set; } = 1;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Breakout55WithFilters_RiskDollars";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                BarsRequiredToTrade = 60;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(AtrPeriod);
                adx = ADX(AdxPeriod);

                if (TrendEmaPeriod > 0)
                    trendEma = EMA(TrendEmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(BarsRequiredToTrade, Lookback + 2))
                return;

            // ===== Imprimir fecha por sesión (control backtest) =====
            if (Bars.IsFirstBarOfSession)
            {
                int yyyymmdd = ToDay(Time[0]);
                if (yyyymmdd != lastSessionDate)
                {
                    lastSessionDate = yyyymmdd;
                    tradedToday = false;
                    Print($"Backtest Day: {Time[0]:yyyy-MM-dd}");
                }
            }

            // ===== Forzar cierre por hora (opcional) =====
            if (UseCloseTime && ToTime(Time[0]) >= HHmmToIntTime(CloseHHmm))
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("CloseTimeExit", "LongBreakout");

                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("CloseTimeExit", "ShortBreakout");

                return;
            }

            // ===== Ventana horaria (opcional) =====
            if (UseTimeWindow)
            {
                int now = ToTime(Time[0]);
                int start = HHmmToIntTime(StartHHmm);
                int end = HHmmToIntTime(EndHHmm);

                bool inWindow = (start <= end) ? (now >= start && now <= end) : (now >= start || now <= end);
                if (!inWindow)
                    return;
            }

            // ===== 1 trade por día (opcional) =====
            if (OneTradePerDay && tradedToday)
                return;

            // ===== Filtro ADX (opcional): ADX <= AdxMax =====
            if (UseAdxFilter && adx[0] > AdxMax)
                return;

            // ===== Breakout levels (excluyendo vela actual) =====
            double prevHigh = MAX(High, Lookback)[1];
            double prevLow  = MIN(Low, Lookback)[1];

            // ===== SL ticks desde ATR =====
            double atrValue = atr[0];
            int slTicks = PriceToTicks(atrValue * AtrMultSL);
            slTicks = Math.Max(1, slTicks);

            // ===== Qty automático por riesgo en dólares =====
            int qty = CalculateQtyFromRisk(slTicks, RiskDollars, MaxContracts);
            if (qty < 1)
                return; // con el SL actual (ATR), no se puede respetar el riesgo

            // ===== TP ticks =====
            int tpTicks = (int)Math.Round(slTicks * Rrr, MidpointRounding.AwayFromZero);
            tpTicks = Math.Max(1, tpTicks);

            // Configurar SL/TP (para la próxima entrada)
            SetStopLoss(CalculationMode.Ticks, slTicks);
            SetProfitTarget(CalculationMode.Ticks, tpTicks);

            // ===== Señales =====
            bool breakLong  = AllowLong  && Close[0] > prevHigh && Close[1] <= prevHigh;
            bool breakShort = AllowShort && Close[0] < prevLow  && Close[1] >= prevLow;

            // ===== Filtro EMA (opcional) =====
            if (UseTrendEma && TrendEmaPeriod > 0 && trendEma != null)
            {
                if (breakLong && Close[0] <= trendEma[0])  breakLong = false;
                if (breakShort && Close[0] >= trendEma[0]) breakShort = false;
            }

            // Si ya hay posición, sólo gestionar BE
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManageBreakEven();
                return;
            }

            // ===== Entradas Market con qty auto =====
            if (breakLong)
            {
                EnterLong(qty, "LongBreakout");
                tradedToday = true;
            }
            else if (breakShort)
            {
                EnterShort(qty, "ShortBreakout");
                tradedToday = true;
            }

            ManageBreakEven();
        }

        private int CalculateQtyFromRisk(int slTicks, double riskDollars, int maxContracts)
        {
            if (slTicks <= 0 || riskDollars <= 0 || maxContracts <= 0)
                return 0;

            // TickValue = PointValue * TickSize
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;

            // Riesgo por 1 contrato
            double riskPerContract = slTicks * tickValue;

            if (riskPerContract <= 0)
                return 0;

            int qty = (int)Math.Floor(riskDollars / riskPerContract);

            if (qty < 1)
                return 0;

            return Math.Min(qty, maxContracts);
        }

        private void ManageBreakEven()
        {
            if (!UseBreakEven)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double entry = Position.AveragePrice;
                double triggerPrice = entry + (BeTriggerTicks * TickSize);

                if (Close[0] >= triggerPrice)
                {
                    double beStopPrice = entry + (BeOffsetTicks * TickSize);
                    SetStopLoss(CalculationMode.Price, beStopPrice);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double entry = Position.AveragePrice;
                double triggerPrice = entry - (BeTriggerTicks * TickSize);

                if (Close[0] <= triggerPrice)
                {
                    double beStopPrice = entry - (BeOffsetTicks * TickSize);
                    SetStopLoss(CalculationMode.Price, beStopPrice);
                }
            }
        }

        private int HHmmToIntTime(int hhmm)
        {
            int hh = hhmm / 100;
            int mm = hhmm % 100;

            hh = Math.Max(0, Math.Min(23, hh));
            mm = Math.Max(0, Math.Min(59, mm));

            return (hh * 10000) + (mm * 100);
        }

        private int PriceToTicks(double priceDistance)
        {
            return (int)Math.Round(priceDistance / TickSize, MidpointRounding.AwayFromZero);
        }
    }
}
