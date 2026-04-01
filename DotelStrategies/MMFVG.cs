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
    public class TimeConfirmThenMicroTrigger_30mWindow : Strategy
    {
        // ====== Indicadores (micro) ======
        private ATR atrMicro;
        private EMA emaMicro;

        // ====== Control diario / estado ======
        private bool tradedToday = false;
        private bool microArmed = false;

        // Sesgo (contrario a la vela macro)
        private bool biasLong = false;            // true = buscar LONG, false = buscar SHORT
        private int macroSignalDate = -1;         // yyyyMMdd

        // Ventana de trading micro
        private DateTime windowStart = Core.Globals.MinDate;
        private DateTime windowEnd   = Core.Globals.MinDate;

        // ====== Gestión de órdenes SL/TP (pendientes) ======
        private string entrySignal = string.Empty;
        private bool exitsSubmitted = false;

        // ====== Risk sizing ======
        private int plannedQty = 1;

        // ====== ENUMS ======
        public enum TradeMode
        {
            Both = 0,
            LongOnly = 1,
            ShortOnly = 2
        }

        public enum TriggerMode
        {
            EMA = 0,
            FVG = 1
        }

        // =======================
        // Inputs
        // =======================
        [NinjaScriptProperty]
        [Display(Name = "Modo (Both/LongOnly/ShortOnly)", Order = 0, GroupName = "Parámetros")]
        public TradeMode Mode { get; set; } = TradeMode.Both;

        [NinjaScriptProperty]
        [Display(Name = "Trigger (EMA/FVG)", Order = 1, GroupName = "Parámetros")]
        public TriggerMode Trigger { get; set; } = TriggerMode.EMA;

        [NinjaScriptProperty]
        [Display(Name = "Hora Vela Dirección (HHmm)", Order = 2, GroupName = "Parámetros")]
        public int TargetTimeHHmm { get; set; } = 1530;

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "Ventana (minutos)", Order = 3, GroupName = "Parámetros")]
        public int WindowMinutes { get; set; } = 30;

        // EMA trigger
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Micro Period", Order = 4, GroupName = "Micro (1m)")]
        public int EMAPeriod { get; set; } = 10;

        // ATR / TP
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period (Micro)", Order = 5, GroupName = "Micro (1m)")]
        public int ATRPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Múltiplo (Micro)", Order = 6, GroupName = "Micro (1m)")]
        public double ATRMult { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "TP Mult (sobre SL)", Order = 7, GroupName = "Micro (1m)")]
        public double TPMult { get; set; } = 1.5;

        // ===== NUEVO: RIESGO FIJO EN DINERO =====
        [NinjaScriptProperty]
        [Range(1, double.MaxValue)]
        [Display(Name = "Riesgo $ por trade", Order = 8, GroupName = "Risk")]
        public double RiskDollars { get; set; } = 100;

        [NinjaScriptProperty]
        [Display(Name = "Imprimir Debug", Order = 9, GroupName = "Debug")]
        public bool PrintDebug { get; set; } = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TimeConfirmThenMicroTrigger_30mWindow";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                // Micro: 1 minuto
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                emaMicro = EMA(BarsArray[1], EMAPeriod);
                atrMicro = ATR(BarsArray[1], ATRPeriod);
            }
        }

        // Calcula quantity por riesgo fijo usando el ATRDist actual como SL
        private int CalculateQtyByRisk(double atrDist)
        {
            if (atrDist <= 0 || TickSize <= 0)
                return 1;

            // ticks de stop
            double stopTicksD = atrDist / TickSize;
            if (stopTicksD <= 0)
                return 1;

            // valor por tick (futuros típicamente)
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            if (tickValue <= 0)
                return 1;

            double riskPerContract = stopTicksD * tickValue;
            if (riskPerContract <= 0)
                return 1;

            int qty = (int)Math.Floor(RiskDollars / riskPerContract);
            if (qty < 1)
                qty = 1;

            if (PrintDebug)
            {
                Print($"{Times[1][0]} => RiskSizing | Risk$={RiskDollars:F2} | atrDist={atrDist:F5} | stopTicks={stopTicksD:F2} | tickValue={tickValue:F2} | risk/ctrt={riskPerContract:F2} | qty={qty}");
                if (RiskDollars < riskPerContract)
                    Print($"{Times[1][0]} => Aviso: Risk$ < riesgo de 1 contrato. No se puede ajustar exacto; se usará qty=1.");
            }

            return qty;
        }

        protected override void OnBarUpdate()
        {
            // Seguridad: barras suficientes en ambas series
            if (CurrentBars[0] < 10 || CurrentBars[1] < Math.Max(Math.Max(ATRPeriod + 3, EMAPeriod + 3), 10))
                return;

            // =========================
            // Reset diario (en macro)
            // =========================
            if (BarsInProgress == 0 && Bars.IsFirstBarOfSession)
            {
                tradedToday = false;
                microArmed = false;
                biasLong = false;
                macroSignalDate = -1;

                windowStart = Core.Globals.MinDate;
                windowEnd   = Core.Globals.MinDate;

                entrySignal = string.Empty;
                exitsSubmitted = false;

                plannedQty = 1;

                if (PrintDebug)
                    Print($"{Time[0]:yyyy-MM-dd} => Reset diario.");
            }

            // =========================
            // 1) Confirmación MACRO (BIP 0)
            // =========================
            if (BarsInProgress == 0)
            {
                if (tradedToday)
                    return;

                int today = ToDay(Time[0]); // yyyyMMdd
                if (macroSignalDate == today)
                    return;

                int hhmmOnly = ToTime(Time[0]) / 100; // HHmm (hora de APERTURA de la vela)
                if (hhmmOnly != TargetTimeHHmm)
                    return;

                bool isBearish = Close[0] < Open[0];
                bool isBullish = Close[0] > Open[0];

                if (!isBearish && !isBullish)
                {
                    if (PrintDebug) Print($"{Time[0]} => Doji en hora dirección, no armamos ventana.");
                    macroSignalDate = today;
                    return;
                }

                // Operamos EN CONTRA de la vela dirección:
                biasLong = isBearish; // vela bajista => sesgo LONG, vela alcista => sesgo SHORT

                // Respeta modo
                if (biasLong && Mode == TradeMode.ShortOnly)
                {
                    if (PrintDebug) Print($"{Time[0]} => Sesgo LONG ignorado (Mode=ShortOnly).");
                    macroSignalDate = today;
                    return;
                }
                if (!biasLong && Mode == TradeMode.LongOnly)
                {
                    if (PrintDebug) Print($"{Time[0]} => Sesgo SHORT ignorado (Mode=LongOnly).");
                    macroSignalDate = today;
                    return;
                }

                windowStart = Time[0];
                windowEnd   = Time[0].AddMinutes(WindowMinutes);

                microArmed = true;
                macroSignalDate = today;

                if (PrintDebug)
                    Print($"{Time[0]} => Dirección OK. Ventana: {windowStart:HH:mm}-{windowEnd:HH:mm} | bias={(biasLong ? "LONG" : "SHORT")} | Trigger={Trigger}");

                return;
            }

            // =========================
            // 2) ENTRADA MICRO (BIP 1 = 1 minuto)
            // =========================
            if (BarsInProgress != 1)
                return;

            if (!microArmed || tradedToday)
                return;

            DateTime tMicro = Times[1][0];

            // Fin de ventana => desarmar
            if (tMicro > windowEnd)
            {
                microArmed = false;
                if (PrintDebug) Print($"{tMicro} => Fin de ventana. Se desarma micro.");
                return;
            }

            // Aún no empieza la ventana
            if (tMicro < windowStart)
                return;

            // Solo si estamos flat
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            bool triggerOk = false;

            // ===== Trigger EMA =====
            if (Trigger == TriggerMode.EMA)
            {
                double close0 = Closes[1][0];
                double close1 = Closes[1][1];
                double ema0   = emaMicro[0];
                double ema1   = emaMicro[1];

                bool crossUp   = close1 <= ema1 && close0 > ema0;
                bool crossDown = close1 >= ema1 && close0 < ema0;

                triggerOk = biasLong ? crossUp : crossDown;

                if (PrintDebug && triggerOk)
                    Print($"{tMicro} => Trigger EMA OK ({(biasLong ? "CrossUp" : "CrossDown")}).");
            }
            // ===== Trigger FVG a favor =====
            else if (Trigger == TriggerMode.FVG)
            {
                double low0  = Lows[1][0];
                double high0 = Highs[1][0];

                double high2 = Highs[1][2];
                double low2  = Lows[1][2];

                bool bullishFvg = low0 > high2;
                bool bearishFvg = high0 < low2;

                triggerOk = biasLong ? bullishFvg : bearishFvg;

                if (PrintDebug && triggerOk)
                    Print($"{tMicro} => Trigger FVG OK ({(biasLong ? "BullishFVG" : "BearishFVG")}).");
            }

            if (!triggerOk)
                return;

            // ====== CALCULAR QTY POR RIESGO FIJO (usando SL = ATRDist actual) ======
            double atrDistNow = atrMicro[0] * ATRMult;
            plannedQty = CalculateQtyByRisk(atrDistNow);

            // Ejecutar entrada (1 trade/día)
            if (biasLong)
            {
                entrySignal = "MicroLong";
                if (PrintDebug) Print($"{tMicro} => ENTER LONG qty={plannedQty} (Trigger={Trigger})");
                EnterLong(plannedQty, entrySignal);
            }
            else
            {
                entrySignal = "MicroShort";
                if (PrintDebug) Print($"{tMicro} => ENTER SHORT qty={plannedQty} (Trigger={Trigger})");
                EnterShort(plannedQty, entrySignal);
            }

            tradedToday = true;
            microArmed = false;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution?.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            if (exitsSubmitted)
                return;

            bool isEntryLong  = execution.Order.Name == "MicroLong";
            bool isEntryShort = execution.Order.Name == "MicroShort";

            if (!isEntryLong && !isEntryShort)
                return;

            double atrDist = atrMicro[0] * ATRMult;
            if (atrDist <= 0)
                return;

            double entryPrice = execution.Price;

            if (isEntryLong)
            {
                double stopPrice   = entryPrice - atrDist;
                double targetPrice = entryPrice + (atrDist * TPMult);

                if (PrintDebug)
                    Print($"{time} => FILL LONG @ {entryPrice} | Q={execution.Quantity} | SL={stopPrice} | TP={targetPrice} | ATRdist={atrDist}");

                ExitLongStopMarket(0, true, execution.Quantity, stopPrice, "SL_Long", "MicroLong");
                ExitLongLimit(0, true, execution.Quantity, targetPrice, "TP_Long", "MicroLong");
            }
            else
            {
                double stopPrice   = entryPrice + atrDist;
                double targetPrice = entryPrice - (atrDist * TPMult);

                if (PrintDebug)
                    Print($"{time} => FILL SHORT @ {entryPrice} | Q={execution.Quantity} | SL={stopPrice} | TP={targetPrice} | ATRdist={atrDist}");

                ExitShortStopMarket(0, true, execution.Quantity, stopPrice, "SL_Short", "MicroShort");
                ExitShortLimit(0, true, execution.Quantity, targetPrice, "TP_Short", "MicroShort");
            }

            exitsSubmitted = true;
        }
    }
}
