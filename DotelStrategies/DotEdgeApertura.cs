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
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class TimeATRHoldBarsBot : Strategy
    {
        private ATR atr;

        private bool tradedToday = false;

        // Control salida por velas
        private int entryBarIndex = -1;
        private string activeEntrySignal = string.Empty;

        // =========================
        // ENUM: modo de operación
        // =========================
        public enum TradeMode
        {
            Both = 0,
            LongOnly = 1,
            ShortOnly = 2
        }

        [NinjaScriptProperty]
        [Display(Name = "Modo (Both/LongOnly/ShortOnly)", Order = 0, GroupName = "Parámetros")]
        public TradeMode Mode { get; set; } = TradeMode.Both;

        [NinjaScriptProperty]
        [Display(Name = "Hora Objetivo (HHmm)", Order = 1, GroupName = "Parámetros")]
        public int TargetTimeHHmm { get; set; } = 1530;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "HoldBars (velas a mantener)", Order = 2, GroupName = "Parámetros")]
        public int HoldBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Order = 3, GroupName = "ATR")]
        public int ATRPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Múltiplo", Order = 4, GroupName = "ATR")]
        public double ATRMult { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Display(Name = "Imprimir Debug", Order = 5, GroupName = "Debug")]
        public bool PrintDebug { get; set; } = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TimeATRHoldBarsBot";
                Calculate = Calculate.OnBarClose;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(ATRPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(ATRPeriod + 2, 10))
                return;

            // Reset diario
            if (Bars.IsFirstBarOfSession)
            {
                tradedToday = false;
                entryBarIndex = -1;
                activeEntrySignal = string.Empty;

                if (PrintDebug)
                    Print($"{Time[0]:yyyy-MM-dd} => Nuevo día/sesión");
            }

            // 1) Salida por número de velas
            if (Position.MarketPosition != MarketPosition.Flat && entryBarIndex >= 0)
            {
                int barsHeld = CurrentBar - entryBarIndex;

                if (barsHeld >= HoldBars)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        if (PrintDebug) Print($"{Time[0]} => EXIT LONG por HoldBars={HoldBars}");
                        ExitLong("TimeExitLong", activeEntrySignal);
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        if (PrintDebug) Print($"{Time[0]} => EXIT SHORT por HoldBars={HoldBars}");
                        ExitShort("TimeExitShort", activeEntrySignal);
                    }
                    return;
                }
            }

            // 2) Entrada: 1 trade/día y solo si estamos flat
            if (tradedToday || Position.MarketPosition != MarketPosition.Flat)
                return;

            // Vela de la hora objetivo (comparando la hora de apertura de la vela actual)
            int hhmmss = ToTime(Time[0]); // HHmmss
            int hhmmOnly = hhmmss / 100;  // HHmm

            if (hhmmOnly != TargetTimeHHmm)
                return;

            bool isBearish = Close[0] < Open[0];
            bool isBullish = Close[0] > Open[0];

            // Si es doji, no operamos
            if (!isBearish && !isBullish)
            {
                if (PrintDebug) Print($"{Time[0]} => Doji en hora objetivo, no trade.");
                tradedToday = true;
                return;
            }

            double atrDistance = atr[0] * ATRMult;

            // Reglas:
            // - vela bajista => COMPRAR
            // - vela alcista => VENDER
            if (isBearish)
            {
                if (Mode == TradeMode.ShortOnly)
                {
                    if (PrintDebug) Print($"{Time[0]} => Señal LONG ignorada (Mode=ShortOnly).");
                    tradedToday = true;
                    return;
                }

                activeEntrySignal = "TimeEntryLong";
                double stopPrice = Close[0] - atrDistance;

                if (PrintDebug)
                    Print($"{Time[0]} => BEARISH => ENTER LONG | ATRdist={atrDistance:F5} | SL={stopPrice:F5}");

                SetStopLoss(activeEntrySignal, CalculationMode.Price, stopPrice, false);
                EnterLong(1, activeEntrySignal);
            }
            else // bullish
            {
                if (Mode == TradeMode.LongOnly)
                {
                    if (PrintDebug) Print($"{Time[0]} => Señal SHORT ignorada (Mode=LongOnly).");
                    tradedToday = true;
                    return;
                }

                activeEntrySignal = "TimeEntryShort";
                double stopPrice = Close[0] + atrDistance;

                if (PrintDebug)
                    Print($"{Time[0]} => BULLISH => ENTER SHORT | ATRdist={atrDistance:F5} | SL={stopPrice:F5}");

                SetStopLoss(activeEntrySignal, CalculationMode.Price, stopPrice, false);
                EnterShort(1, activeEntrySignal);
            }

            tradedToday = true;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution?.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            // Marcamos la vela de entrada para contar HoldBars
            if (execution.Order.Name == "TimeEntryLong" || execution.Order.Name == "TimeEntryShort")
            {
                entryBarIndex = CurrentBar;

                if (PrintDebug)
                    Print($"{time} => FILLED {execution.Order.Name} @ {price} | entryBarIndex={entryBarIndex}");
            }
        }
    }
}
