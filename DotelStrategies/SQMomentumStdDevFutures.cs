#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SQ_Momentum_StdDev_Futures : Strategy
    {
        #region Parameters

        [NinjaScriptProperty]
        [Display(Name = "MagicNumber", Order = 1, GroupName = "Parameters")]
        public int MagicNumber { get; set; } = 11111;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MomPeriod1", Order = 2, GroupName = "Parameters")]
        public int MomPeriod1 { get; set; } = 40;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "StdDevChangesUpPrd1", Order = 3, GroupName = "Parameters")]
        public int StdDevChangesUpPrd1 { get; set; } = 30;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "StdDevRisingPeriod1", Order = 4, GroupName = "Parameters")]
        public int StdDevRisingPeriod1 { get; set; } = 51;

        [NinjaScriptProperty]
        [Display(Name = "PriceEntryMult1", Order = 5, GroupName = "Parameters")]
        public double PriceEntryMult1 { get; set; } = 0.9;

        [NinjaScriptProperty]
        [Display(Name = "MoveSL2BECoef1", Order = 6, GroupName = "Parameters")]
        public double MoveSL2BECoef1 { get; set; } = 4.9;

        [NinjaScriptProperty]
        [Display(Name = "ProfitTargetPct1", Order = 7, GroupName = "Parameters")]
        public double ProfitTargetPct1 { get; set; } = 6.7;

        [NinjaScriptProperty]
        [Display(Name = "StopLossDistance1", Order = 8, GroupName = "Parameters")]
        public double StopLossDistance1 { get; set; } = 115.0;

        [NinjaScriptProperty]
        [Display(Name = "EnableFridayExit", Order = 9, GroupName = "Trading Options")]
        public bool EnableFridayExit { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "FridayExitTimeHHmm", Order = 10, GroupName = "Trading Options")]
        public int FridayExitTimeHHmm { get; set; } = 38; // 00:38

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Order = 11, GroupName = "Money Management")]
        public int Quantity { get; set; } = 1;

        #endregion

        #region Private fields

        private Momentum momentum;
        private StdDev stdDevChanges;
        private StdDev stdDevRising;
        private ATR atrEntry;
        private ATR atrBE;

        private double currentLongStopPrice;
        private double currentShortStopPrice;
        private double currentLongTargetPrice;
        private double currentShortTargetPrice;
        private bool longMovedToBE;
        private bool shortMovedToBE;

        private const string LongSignal = "SQ_Long";
        private const string ShortSignal = "SQ_Short";
        private const string LongExitStopSignal = "SQ_LongExitStop";
        private const string LongExitTargetSignal = "SQ_LongExitTarget";
        private const string ShortExitStopSignal = "SQ_ShortExitStop";
        private const string ShortExitTargetSignal = "SQ_ShortExitTarget";

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SQ_Momentum_StdDev_Futures";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                BarsRequiredToTrade = 70;
                TraceOrders = false;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                momentum      = Momentum(Close, MomPeriod1);
                stdDevChanges = StdDev(Typical, StdDevChangesUpPrd1);
                stdDevRising  = StdDev(Open, StdDevRisingPeriod1);
                atrEntry      = ATR(20);
                atrBE         = ATR(65);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Emular "On Bar Open"
            if (!IsFirstTickOfBar)
            {
                ManageOpenPosition();
                return;
            }

            ManageOpenPosition();

            // Salida en viernes a la hora indicada
            if (EnableFridayExit && IsFridayExitTime())
            {
                CancelAllPendingEntries();

                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("FridayExitLong", LongSignal);

                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("FridayExitShort", ShortSignal);

                return;
            }

            bool longEntrySignal  = IsMomentumRisingAt2();
            bool shortEntrySignal = IsMomentumFallingAt2();

            bool longExitSignal   = IsStdDevExitSignal();
            bool shortExitSignal  = IsStdDevExitSignal();

            // Exits por señal
            if (Position.MarketPosition == MarketPosition.Long && longExitSignal && !longEntrySignal)
            {
                CancelAllPendingEntries();
                ExitLong("RuleExitLong", LongSignal);
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short && shortExitSignal && !shortEntrySignal)
            {
                CancelAllPendingEntries();
                ExitShort("RuleExitShort", ShortSignal);
                return;
            }

            // Solo buscamos nuevas entradas si estamos flat
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double haClose1 = GetHeikenAshiClose(1);
            double atr20_2 = atrEntry[2];

            // Long entry
            if (longEntrySignal)
            {
                double stopPrice = haClose1 + (PriceEntryMult1 * atr20_2);
                stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

                // Reemplazo permitido: reenviamos cada nueva barra
                EnterLongStopMarket(Quantity, stopPrice, LongSignal);
            }

            // Short entry
            if (shortEntrySignal && !longEntrySignal)
            {
                double stopPrice = haClose1 - (PriceEntryMult1 * atr20_2);
                stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

                EnterShortStopMarket(Quantity, stopPrice, ShortSignal);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            if (execution.Order.Name == LongSignal)
            {
                double avgPrice = execution.Order.AverageFillPrice;

                currentLongStopPrice   = Instrument.MasterInstrument.RoundToTickSize(avgPrice - StopLossDistance1);
                currentLongTargetPrice = Instrument.MasterInstrument.RoundToTickSize(avgPrice * (1.0 + ProfitTargetPct1 / 100.0));
                longMovedToBE = false;

                ExitLongStopMarket(0, true, execution.Order.Quantity, currentLongStopPrice, LongExitStopSignal, LongSignal);
                ExitLongLimit(0, true, execution.Order.Quantity, currentLongTargetPrice, LongExitTargetSignal, LongSignal);
            }
            else if (execution.Order.Name == ShortSignal)
            {
                double avgPrice = execution.Order.AverageFillPrice;

                currentShortStopPrice   = Instrument.MasterInstrument.RoundToTickSize(avgPrice + StopLossDistance1);
                currentShortTargetPrice = Instrument.MasterInstrument.RoundToTickSize(avgPrice * (1.0 - ProfitTargetPct1 / 100.0));
                shortMovedToBE = false;

                ExitShortStopMarket(0, true, execution.Order.Quantity, currentShortStopPrice, ShortExitStopSignal, ShortSignal);
                ExitShortLimit(0, true, execution.Order.Quantity, currentShortTargetPrice, ShortExitTargetSignal, ShortSignal);
            }
        }

        private void ManageOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double beTrigger = MoveSL2BECoef1 * atrBE[0];

                if (!longMovedToBE && Close[0] >= Position.AveragePrice + beTrigger)
                {
                    currentLongStopPrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice);
                    longMovedToBE = true;
                }

                ExitLongStopMarket(0, true, Position.Quantity, currentLongStopPrice, LongExitStopSignal, LongSignal);
                ExitLongLimit(0, true, Position.Quantity, currentLongTargetPrice, LongExitTargetSignal, LongSignal);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double beTrigger = MoveSL2BECoef1 * atrBE[0];

                if (!shortMovedToBE && Close[0] <= Position.AveragePrice - beTrigger)
                {
                    currentShortStopPrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice);
                    shortMovedToBE = true;
                }

                ExitShortStopMarket(0, true, Position.Quantity, currentShortStopPrice, ShortExitStopSignal, ShortSignal);
                ExitShortLimit(0, true, Position.Quantity, currentShortTargetPrice, ShortExitTargetSignal, ShortSignal);
            }
        }

        private bool IsMomentumRisingAt2()
        {
            // Momentum(...)[2] is rising
            return momentum[2] > momentum[3];
        }

        private bool IsMomentumFallingAt2()
        {
            // Momentum(...)[2] is falling
            return momentum[2] < momentum[3];
        }

        private bool IsStdDevExitSignal()
        {
            // (StdDev(...)[1] changes direction upwards) AND (StdDev(...)[1] is rising)
            bool changesDirectionUp = stdDevChanges[1] > stdDevChanges[2] && stdDevChanges[2] <= stdDevChanges[3];
            bool risingNow          = stdDevRising[1] > stdDevRising[2];

            return changesDirectionUp && risingNow;
        }

        private double GetHeikenAshiClose(int barsAgo)
        {
            // HA Close = (Open + High + Low + Close) / 4
            return (Open[barsAgo] + High[barsAgo] + Low[barsAgo] + Close[barsAgo]) / 4.0;
        }

        private bool IsFridayExitTime()
        {
            DateTime barTime = Times[0][0];

            if (barTime.DayOfWeek != DayOfWeek.Friday)
                return false;

            int currentHHmm = ToTime(barTime) / 100;

            return currentHHmm >= FridayExitTimeHHmm;
        }

        private void CancelAllPendingEntries()
        {
            // En Managed approach, al dejar de reenviar la orden, normalmente se cancela en la siguiente barra.
            // Este método queda por claridad estructural.
        }
    }
}