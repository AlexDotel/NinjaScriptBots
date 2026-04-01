#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class InsideBarBreakoutSqxBot : Strategy
    {
        private const string LongSignalName = "InsideBarLong";
        private const string ShortSignalName = "InsideBarShort";

        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int pendingOrdersCreationBar;
        private bool longBreakEvenMoved;
        private bool shortBreakEvenMoved;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Version simple de inside bar breakout: ordenes stop, caducidad por barras, SL/TP por rango y break even opcional.";
                Name = "InsideBarBreakoutSqxBot";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                BarsRequiredToTrade = 3;
                TraceOrders = false;
                DefaultQuantity = 1;

                EntryQuantity = 1;
                EntryOffsetTicks = 1;
                BarsValid = 20;
                StopLossRangeMultiplier = 0.5;
                ProfitTargetRangeMultiplier = 2.0;
                UseBreakEven = true;
                BreakEvenTriggerTicks = 10;
                BreakEvenPlusTicks = 0;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();
                ResetRuntimeState();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 2)
                return;

            ManageBreakEven();

            if (!IsFirstTickOfBar)
                return;

            ExpirePendingEntriesIfNeeded();

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (!IsInsideBarSetup())
                return;

            SubmitInsideBarOrders();
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            if (order.Name == LongSignalName)
                longEntryOrder = IsFinalState(orderState) ? null : order;
            else if (order.Name == ShortSignalName)
                shortEntryOrder = IsFinalState(orderState) ? null : order;

            if ((order.Name == LongSignalName || order.Name == ShortSignalName) && error != ErrorCode.NoError)
            {
                Print(string.Format(
                    "{0} | Error en orden {1}: {2} {3}",
                    time,
                    order.Name,
                    error,
                    comment));
            }

            if (!HasWorkingEntryOrders() && Position.MarketPosition == MarketPosition.Flat)
                pendingOrdersCreationBar = -1;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            if (execution.Order.Name == LongSignalName)
            {
                CancelPendingEntry(shortEntryOrder);
                pendingOrdersCreationBar = -1;
                longBreakEvenMoved = false;
                shortBreakEvenMoved = false;
                return;
            }

            if (execution.Order.Name == ShortSignalName)
            {
                CancelPendingEntry(longEntryOrder);
                pendingOrdersCreationBar = -1;
                longBreakEvenMoved = false;
                shortBreakEvenMoved = false;
                return;
            }

            if (marketPosition == MarketPosition.Flat)
            {
                longBreakEvenMoved = false;
                shortBreakEvenMoved = false;
            }
        }

        private void ValidateConfiguration()
        {
            if (EntryQuantity <= 0)
                throw new ArgumentOutOfRangeException("EntryQuantity", "EntryQuantity debe ser mayor o igual que 1.");

            if (EntryOffsetTicks < 0)
                throw new ArgumentOutOfRangeException("EntryOffsetTicks", "EntryOffsetTicks no puede ser negativo.");

            if (BarsValid <= 0)
                throw new ArgumentOutOfRangeException("BarsValid", "BarsValid debe ser mayor que 0.");

            if (StopLossRangeMultiplier <= 0)
                throw new ArgumentOutOfRangeException("StopLossRangeMultiplier", "StopLossRangeMultiplier debe ser mayor que 0.");

            if (ProfitTargetRangeMultiplier <= 0)
                throw new ArgumentOutOfRangeException("ProfitTargetRangeMultiplier", "ProfitTargetRangeMultiplier debe ser mayor que 0.");

            if (BreakEvenTriggerTicks < 0)
                throw new ArgumentOutOfRangeException("BreakEvenTriggerTicks", "BreakEvenTriggerTicks no puede ser negativo.");

            if (BreakEvenPlusTicks < 0)
                throw new ArgumentOutOfRangeException("BreakEvenPlusTicks", "BreakEvenPlusTicks no puede ser negativo.");
        }

        private void ResetRuntimeState()
        {
            longEntryOrder = null;
            shortEntryOrder = null;
            pendingOrdersCreationBar = -1;
            longBreakEvenMoved = false;
            shortBreakEvenMoved = false;
        }

        private void ManageBreakEven()
        {
            if (!UseBreakEven || BreakEvenTriggerTicks <= 0)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (longBreakEvenMoved)
                    return;

                double triggerPrice = Position.AveragePrice + (BreakEvenTriggerTicks * TickSize);
                if (High[0] < triggerPrice)
                    return;

                double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Position.AveragePrice + (BreakEvenPlusTicks * TickSize));

                SetStopLoss(LongSignalName, CalculationMode.Price, breakEvenPrice, false);
                longBreakEvenMoved = true;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (shortBreakEvenMoved)
                    return;

                double triggerPrice = Position.AveragePrice - (BreakEvenTriggerTicks * TickSize);
                if (Low[0] > triggerPrice)
                    return;

                double breakEvenPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Position.AveragePrice - (BreakEvenPlusTicks * TickSize));

                SetStopLoss(ShortSignalName, CalculationMode.Price, breakEvenPrice, false);
                shortBreakEvenMoved = true;
                return;
            }

            longBreakEvenMoved = false;
            shortBreakEvenMoved = false;
        }

        private void ExpirePendingEntriesIfNeeded()
        {
            if (!HasWorkingEntryOrders() || pendingOrdersCreationBar < 0)
                return;

            if (CurrentBar - pendingOrdersCreationBar < BarsValid)
                return;

            CancelPendingEntries();
            pendingOrdersCreationBar = -1;
        }

        private bool IsInsideBarSetup()
        {
            return High[1] < High[2] && Low[1] > Low[2];
        }

        private void SubmitInsideBarOrders()
        {
            double insideBarRange = High[1] - Low[1];
            if (insideBarRange <= 0)
                return;

            CancelPendingEntries();

            int stopLossTicks = Math.Max(1, (int)Math.Round((insideBarRange * StopLossRangeMultiplier) / TickSize));
            int profitTargetTicks = Math.Max(1, (int)Math.Round((insideBarRange * ProfitTargetRangeMultiplier) / TickSize));

            double longStopPrice = Instrument.MasterInstrument.RoundToTickSize(High[1] + (EntryOffsetTicks * TickSize));
            double shortStopPrice = Instrument.MasterInstrument.RoundToTickSize(Low[1] - (EntryOffsetTicks * TickSize));

            SetStopLoss(LongSignalName, CalculationMode.Ticks, stopLossTicks, false);
            SetProfitTarget(LongSignalName, CalculationMode.Ticks, profitTargetTicks);
            SetStopLoss(ShortSignalName, CalculationMode.Ticks, stopLossTicks, false);
            SetProfitTarget(ShortSignalName, CalculationMode.Ticks, profitTargetTicks);

            EnterLongStopMarket(EntryQuantity, longStopPrice, LongSignalName);
            EnterShortStopMarket(EntryQuantity, shortStopPrice, ShortSignalName);

            pendingOrdersCreationBar = CurrentBar;
        }

        private void CancelPendingEntries()
        {
            CancelPendingEntry(longEntryOrder);
            CancelPendingEntry(shortEntryOrder);
        }

        private void CancelPendingEntry(Order order)
        {
            if (!IsWorkingOrder(order))
                return;

            CancelOrder(order);
        }

        private bool HasWorkingEntryOrders()
        {
            return IsWorkingOrder(longEntryOrder) || IsWorkingOrder(shortEntryOrder);
        }

        private bool IsWorkingOrder(Order order)
        {
            if (order == null)
                return false;

            return order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.PartFilled
                || order.OrderState == OrderState.Submitted
                || order.OrderState == OrderState.Working;
        }

        private bool IsFinalState(OrderState orderState)
        {
            return orderState == OrderState.Cancelled
                || orderState == OrderState.Filled
                || orderState == OrderState.Rejected;
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Cantidad", GroupName = "Parametros", Order = 0)]
        public int EntryQuantity
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset entrada (ticks)", GroupName = "Parametros", Order = 1)]
        public int EntryOffsetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Validez orden (barras)", GroupName = "Parametros", Order = 2)]
        public int BarsValid
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0000001, double.MaxValue)]
        [Display(Name = "SL x rango", GroupName = "Parametros", Order = 3)]
        public double StopLossRangeMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0000001, double.MaxValue)]
        [Display(Name = "TP x rango", GroupName = "Parametros", Order = 4)]
        public double ProfitTargetRangeMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar break even", GroupName = "Parametros", Order = 5)]
        public bool UseBreakEven
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trigger break even (ticks)", GroupName = "Parametros", Order = 6)]
        public int BreakEvenTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset break even (ticks)", GroupName = "Parametros", Order = 7)]
        public int BreakEvenPlusTicks
        { get; set; }
    }
}
