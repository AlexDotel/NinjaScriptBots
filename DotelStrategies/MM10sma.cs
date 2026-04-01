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
    public class TimeConfirmThenMicroSMAEntry_Window30m : Strategy
    {
        // ====== Indicadores (micro) ======
        private ATR atrMicro;
        private SMA smaMicro;

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

        // ====== ENUM: modo de operación ======
        public enum TradeMode
        {
            Both = 0,
            LongOnly = 1,
            ShortOnly = 2
        }

        // =======================
        // Inputs
        // =======================
        [NinjaScriptProperty]
        [Display(Name = "Modo (Both/LongOnly/ShortOnly)", Order = 0, GroupName = "Parámetros")]
        public TradeMode Mode { get; set; } = TradeMode.Both;

        [NinjaScriptProperty]
        [Display(Name = "Hora Vela Dirección (HHmm)", Order = 1, GroupName = "Parámetros")]
        public int TargetTimeHHmm { get; set; } = 1530;

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "Ventana (minutos)", Order = 2, GroupName = "Parámetros")]
        public int WindowMinutes { get; set; } = 30;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Micro Period", Order = 3, GroupName = "Micro (1m)")]
        public int SMAPeriod { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period (Micro)", Order = 4, GroupName = "Micro (1m)")]
        public int ATRPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Múltiplo (Micro)", Order = 5, GroupName = "Micro (1m)")]
        public double ATRMult { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "TP Mult (sobre SL)", Order = 6, GroupName = "Micro (1m)")]
        public double TPMult { get; set; } = 1.5;

        [NinjaScriptProperty]
        [Display(Name = "Imprimir Debug", Order = 7, GroupName = "Debug")]
        public bool PrintDebug { get; set; } = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TimeConfirmThenMicroSMAEntry_Window30m";
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
                smaMicro = SMA(BarsArray[1], SMAPeriod);
                atrMicro = ATR(BarsArray[1], ATRPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 10 || CurrentBars[1] < Math.Max(ATRPeriod + 2, SMAPeriod + 2))
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

                int hhmmOnly = ToTime(Time[0]) / 100; // HHmm (de la hora de APERTURA de la vela)
                if (hhmmOnly != TargetTimeHHmm)
                    return;

                bool isBearish = Close[0] < Open[0];
                bool isBullish = Close[0] > Open[0];

                if (!isBearish && !isBullish)
                {
                    if (PrintDebug)
                        Print($"{Time[0]} => Doji en hora dirección, no armamos ventana.");
                    macroSignalDate = today;
                    return;
                }

                // Regla: operamos EN CONTRA de la vela dirección
                // - si la vela fue bajista => buscamos LONG
                // - si la vela fue alcista => buscamos SHORT
                biasLong = isBearish;

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

                // Ventana: desde la hora de esa vela hasta +30 minutos (configurable)
                windowStart = Time[0];
                windowEnd = Time[0].AddMinutes(WindowMinutes);

                microArmed = true;
                macroSignalDate = today;

                if (PrintDebug)
                    Print($"{Time[0]} => Dirección confirmada. Ventana micro: {windowStart:HH:mm} - {windowEnd:HH:mm} | bias={(biasLong ? "LONG" : "SHORT")}");

                return;
            }

            // =========================
            // 2) Entrada MICRO (BIP 1 = 1 minuto)
            // =========================
            if (BarsInProgress != 1)
                return;

            if (!microArmed || tradedToday)
                return;

            // Si ya se acabó la ventana, desarmamos y no operamos hoy
            DateTime tMicro = Times[1][0];
            if (tMicro > windowEnd)
            {
                microArmed = false;
                if (PrintDebug)
                    Print($"{tMicro} => Fin de ventana. Se desarma micro hoy.");
                return;
            }

            // Solo buscamos entradas DENTRO de la ventana (incluimos el inicio)
            if (tMicro < windowStart)
                return;

            // Solo si estamos flat
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Cruce del Close con la SMA (micro)
            double close0 = Closes[1][0];
            double close1 = Closes[1][1];
            double sma0   = smaMicro[0];
            double sma1   = smaMicro[1];

            bool crossUp   = close1 <= sma1 && close0 > sma0;
            bool crossDown = close1 >= sma1 && close0 < sma0;

            if (biasLong)
            {
                if (!crossUp)
                    return;

                entrySignal = "MicroLong";

                if (PrintDebug)
                    Print($"{tMicro} => Señal MICRO LONG (cruce arriba SMA) dentro de ventana.");

                EnterLong(1, entrySignal);
            }
            else
            {
                if (!crossDown)
                    return;

                entrySignal = "MicroShort";

                if (PrintDebug)
                    Print($"{tMicro} => Señal MICRO SHORT (cruce abajo SMA) dentro de ventana.");

                EnterShort(1, entrySignal);
            }

            // 1 trade al día
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
                    Print($"{time} => FILL LONG @ {entryPrice} | SL={stopPrice} | TP={targetPrice} | ATRdist={atrDist}");

                // Salidas pendientes OCO ligadas al signal de entrada
                ExitLongStopMarket(0, true, execution.Quantity, stopPrice, "SL_Long", "MicroLong");
                ExitLongLimit(0, true, execution.Quantity, targetPrice, "TP_Long", "MicroLong");
            }
            else
            {
                double stopPrice   = entryPrice + atrDist;
                double targetPrice = entryPrice - (atrDist * TPMult);

                if (PrintDebug)
                    Print($"{time} => FILL SHORT @ {entryPrice} | SL={stopPrice} | TP={targetPrice} | ATRdist={atrDist}");

                ExitShortStopMarket(0, true, execution.Quantity, stopPrice, "SL_Short", "MicroShort");
                ExitShortLimit(0, true, execution.Quantity, targetPrice, "TP_Short", "MicroShort");
            }

            exitsSubmitted = true;
        }
    }
}
