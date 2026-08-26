#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class FirstSessionCandleBot : Strategy
    {
        private const string LongSignalName = "FirstCandleLong";
        private const string ShortSignalName = "FirstCandleShort";

        private int exitOnBar;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos", GroupName = "01. Ejecucion", Order = 0)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir compras", GroupName = "02. Direccion", Order = 0)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir ventas", GroupName = "02. Direccion", Order = 1)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Salir despues de X velas", GroupName = "03. Salida", Order = 0)]
        public int ExitAfterBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000000)]
        [Display(Name = "Take profit al cierre ($)", GroupName = "03. Salida", Order = 1)]
        public double TakeProfitCurrency { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000000)]
        [Display(Name = "Stop loss al cierre ($)", GroupName = "03. Salida", Order = 2)]
        public double StopLossCurrency { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "FirstSessionCandleBot";
                Description = "Opera una vez por sesion segun la primera vela y sale por beneficio, perdida al cierre o numero de velas.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                IncludeTradeHistoryInBacktest = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                TraceOrders = false;
                BarsRequiredToTrade = 0;

                Quantity = 1;
                AllowLongs = true;
                AllowShorts = true;
                ExitAfterBars = 5;
                TakeProfitCurrency = 500;
                StopLossCurrency = 500;
            }
            else if (State == State.DataLoaded)
            {
                exitOnBar = int.MaxValue;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                double unrealizedCurrency = Position.GetUnrealizedProfitLoss(
                    PerformanceUnit.Currency, Close[0]);

                if (StopLossCurrency > 0 && unrealizedCurrency <= -StopLossCurrency)
                {
                    ExitCurrentPosition("StopLossClose");
                }
                else if (TakeProfitCurrency > 0 && unrealizedCurrency >= TakeProfitCurrency)
                {
                    ExitCurrentPosition("TakeProfitClose");
                }
                else if (CurrentBar >= exitOnBar)
                {
                    ExitCurrentPosition("ExitAfterBars");
                }

                return;
            }

            // En la inmensa mayoria de las velas, la ejecucion termina aqui.
            if (!Bars.IsFirstBarOfSession)
                return;

            // Se ejecuta OnBarClose: la primera vela de la sesion ya esta cerrada.
            if (Close[0] > Open[0] && AllowLongs)
            {
                EnterLong(Quantity, LongSignalName);
                exitOnBar = CurrentBar + ExitAfterBars;
            }
            else if (Close[0] < Open[0] && AllowShorts)
            {
                EnterShort(Quantity, ShortSignalName);
                exitOnBar = CurrentBar + ExitAfterBars;
            }
            // Si Open == Close, la primera vela es neutral y no se opera esa sesion.
        }

        private void ExitCurrentPosition(string exitSignalName)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(exitSignalName, LongSignalName);
            else
                ExitShort(exitSignalName, ShortSignalName);
        }
    }
}
