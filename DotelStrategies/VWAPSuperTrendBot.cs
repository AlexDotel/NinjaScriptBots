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

using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class VWAP_SuperTrend_Bot : Strategy
    {
        public enum TradeSideMode
        {
            Both = 0,
            LongOnly = 1,
            ShortOnly = 2
        }

        private Indicators.Dotel.DOTEL_VWAP vwapInd;

        // SuperTrend interno
        private ATR atr;
        private Series<double> finalUpper;
        private Series<double> finalLower;
        private Series<double> superTrend;
        private Series<int> trendDir; // +1 bullish, -1 bearish

        private DateTime lastPrintedDate = Core.Globals.MinDate;

        // Para detectar cruces en modo intrabar (tick)
        private double lastTickPrice = double.NaN;

        #region Inputs

        [NinjaScriptProperty]
        [Display(Name = "Start (HHmm)", Order = 1, GroupName = "Horario")]
        public int StartHHmm { get; set; } = 1530;

        [NinjaScriptProperty]
        [Display(Name = "End (HHmm)", Order = 2, GroupName = "Horario")]
        public int EndHHmm { get; set; } = 1730;

        [NinjaScriptProperty]
        [Display(Name = "Trade Side", Order = 3, GroupName = "Filtros")]
        public TradeSideMode SideMode { get; set; } = TradeSideMode.Both;

        [NinjaScriptProperty]
        [Display(Name = "Use Intrabar (1-tick)", Order = 4, GroupName = "Intrabar")]
        public bool UseIntrabar { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "SuperTrend ATR Period", Order = 1, GroupName = "SuperTrend")]
        public int AtrPeriod { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(0.1, 50)]
        [Display(Name = "SuperTrend Multiplier", Order = 2, GroupName = "SuperTrend")]
        public double AtrMultiplier { get; set; } = 3.0;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "VWAP_SuperTrend_Bot";
                Description = "Cruce de VWAP con SL/Trailing por SuperTrend interno, con filtro horario y modo intrabar opcional.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                // Si intrabar: calculamos en tick y añadimos serie 1-tick para gestión
                if (UseIntrabar)
                {
                    Calculate = Calculate.OnEachTick;
                    AddDataSeries(BarsPeriodType.Tick, 1);
                }
                else
                {
                    Calculate = Calculate.OnBarClose;
                }
            }
            else if (State == State.DataLoaded)
			{
			    // VWAP personalizado
			    vwapInd = DOTEL_VWAP();
			
			    // ATR para SuperTrend
			    atr = ATR(AtrPeriod);
			
			    finalUpper = new Series<double>(this);
			    finalLower = new Series<double>(this);
			    superTrend = new Series<double>(this);
			    trendDir   = new Series<int>(this);
			}

        }

        protected override void OnBarUpdate()
        {
            // Requerimos suficientes barras en la serie principal para ATR
            if (CurrentBars[0] < Math.Max(2, AtrPeriod + 2))
                return;

            // --- 1) Imprimir día por día (progreso backtest) SOLO en BIP 0 ---
            if (BarsInProgress == 0)
                PrintNewDayIfNeeded(Time[0]);

            // --- 2) Actualizar SuperTrend SOLO al cierre de vela principal (BIP 0) ---
            // En intrabar (OnEachTick), OnBarUpdate se llama también para ticks, pero
            // aquí forzamos que el cálculo del ST se haga con barras principales.
            if (BarsInProgress == 0)
                UpdateSuperTrendOnPrimary();

            // --- 3) Gestión/Entradas ---
            if (!UseIntrabar)
            {
                // Modo normal: todo en la serie principal al cierre de vela
                if (BarsInProgress != 0)
                    return;

                if (!IsWithinTimeWindow(ToTime(Time[0])))
                    return;

                ManageTrailingStop(); // actualiza stop en cierre vela

                TryEnterOnVWAPCross_BarClose();
            }
            else
            {
                // Modo intrabar: entradas/gestión en ticks (BIP 1)
                if (BarsInProgress != 1)
                    return;

                // Hora basada en el timestamp del tick
                if (!IsWithinTimeWindow(ToTime(Times[1][0])))
                    return;

                ManageTrailingStop(); // ajusta la orden en cuanto haya ticks (valor ST cambia al cerrar vela BIP 0)

                TryEnterOnVWAPCross_Tick();
            }
        }

        private void PrintNewDayIfNeeded(DateTime t)
        {
            var d = t.Date;
            if (d != lastPrintedDate)
            {
                lastPrintedDate = d;
                Print($"[Backtest Progress] New day: {d:yyyy-MM-dd}");
            }
        }

        private bool IsWithinTimeWindow(int timeHHmmss)
        {
            // ToTime() devuelve HHmmss. Convertimos Start/End HHmm a HHmmss.
            int start = StartHHmm * 100;
            int end   = EndHHmm * 100;

            // Caso normal (ej. 1530 -> 1730)
            if (start <= end)
                return timeHHmmss >= start && timeHHmmss <= end;

            // Caso ventana cruzando medianoche (ej. 2200 -> 0200)
            return timeHHmmss >= start || timeHHmmss <= end;
        }

        private bool AllowLong()  => SideMode == TradeSideMode.Both || SideMode == TradeSideMode.LongOnly;
        private bool AllowShort() => SideMode == TradeSideMode.Both || SideMode == TradeSideMode.ShortOnly;

        // ---------------- SuperTrend ----------------
        private void UpdateSuperTrendOnPrimary()
        {
            // Fuente de cálculo: serie principal (BarsInProgress==0)
            double hl2 = (High[0] + Low[0]) / 2.0;
            double atrVal = atr[0];

            double basicUpper = hl2 + AtrMultiplier * atrVal;
            double basicLower = hl2 - AtrMultiplier * atrVal;

            if (CurrentBars[0] == 0)
            {
                finalUpper[0] = basicUpper;
                finalLower[0] = basicLower;
                trendDir[0]   = +1;
                superTrend[0] = finalLower[0];
                return;
            }

            // Final upper
            if (basicUpper < finalUpper[1] || Close[1] > finalUpper[1])
                finalUpper[0] = basicUpper;
            else
                finalUpper[0] = finalUpper[1];

            // Final lower
            if (basicLower > finalLower[1] || Close[1] < finalLower[1])
                finalLower[0] = basicLower;
            else
                finalLower[0] = finalLower[1];

            int prevDir = trendDir[1];
            int dir = prevDir;

            if (prevDir == +1 && Close[0] < finalLower[0])
                dir = -1;
            else if (prevDir == -1 && Close[0] > finalUpper[0])
                dir = +1;

            trendDir[0] = dir;
            superTrend[0] = (dir == +1) ? finalLower[0] : finalUpper[0];
        }

        private void ManageTrailingStop()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            // superTrend[0] está calculado en la serie principal
            double st = superTrend[0];

            if (Position.MarketPosition == MarketPosition.Long)
            {
                // Stop para largos: por debajo (ST)
                // Ajustamos en cada llamada (en bar-close o en tick según UseIntrabar)
                ExitLongStopMarket(0, true, Position.Quantity, st, "ST_Stop", "LongEntry");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                // Stop para cortos: por encima (ST)
                ExitShortStopMarket(0, true, Position.Quantity, st, "ST_Stop", "ShortEntry");
            }
        }

        // ---------------- Entradas por cruce VWAP ----------------
        private void TryEnterOnVWAPCross_BarClose()
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double vwap = vwapInd.VWAP[0];

            // Cruce usando cierres de vela (bar close)
            bool crossUp   = Close[0] > vwap && Close[1] <= vwapInd.VWAP[1];
            bool crossDown = Close[0] < vwap && Close[1] >= vwapInd.VWAP[1];

            if (crossUp && AllowLong())
            {
                EnterLong(1, "LongEntry");
                // Stop inicial inmediatamente (se irá trail-eando con ManageTrailingStop)
                ExitLongStopMarket(0, true, 1, superTrend[0], "ST_Stop", "LongEntry");
            }
            else if (crossDown && AllowShort())
            {
                EnterShort(1, "ShortEntry");
                ExitShortStopMarket(0, true, 1, superTrend[0], "ST_Stop", "ShortEntry");
            }
        }

        private void TryEnterOnVWAPCross_Tick()
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Precio del tick (serie BIP 1)
            double tickPrice = Closes[1][0];

            // VWAP de la serie principal (último valor disponible)
            double vwap = vwapInd.VWAP[0];

            // Detectar cruce tick-a-tick
            if (double.IsNaN(lastTickPrice))
            {
                lastTickPrice = tickPrice;
                return;
            }

            bool crossUp   = (tickPrice > vwap) && (lastTickPrice <= vwap);
            bool crossDown = (tickPrice < vwap) && (lastTickPrice >= vwap);

            lastTickPrice = tickPrice;

            if (crossUp && AllowLong())
            {
                EnterLong(1, "LongEntry");
                ExitLongStopMarket(0, true, 1, superTrend[0], "ST_Stop", "LongEntry");
            }
            else if (crossDown && AllowShort())
            {
                EnterShort(1, "ShortEntry");
                ExitShortStopMarket(0, true, 1, superTrend[0], "ST_Stop", "ShortEntry");
            }
        }
    }
}
