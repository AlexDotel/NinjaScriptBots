#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CandleSequenceStopEntryBot : Strategy
    {
        private const string LongSignalName = "SeqStopLong";
        private const string ShortSignalName = "SeqStopShort";

        private RSI rsi;
        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int pendingOrderCreationBar;
        private int startMinutes;
        private int endMinutes;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Opera con orden stop tras un bloque de velas en una direccion seguido por otro bloque en direccion contraria. Incluye inversion de logica, filtro RSI y TP como multiplo del SL.";
                Name = "CandleSequenceStopEntryBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 2;
                DefaultQuantity = 1;

                EntryQuantity = 1;
                EnableLongs = true;
                EnableShorts = true;
                BearishBarsCount = 5;
                BullishBarsCount = 5;
                EntryOffsetTicks = 1;
                BarsValid = 1;
                InvertLogic = false;

                UseTimeFilter = false;
                TradingStart = 9.50;
                TradingEnd = 17.00;

                UseRsiFilter = false;
                RsiPeriod = 14;
                RsiLongLevel = 30.0;
                RsiShortLevel = 70.0;
                ConfirmRsiReCross = false;

                StopLossTicks = 20;
                TakeProfitMultiplier = 2.0;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();
                startMinutes = ConvertQuarterHourToMinutes(TradingStart);
                endMinutes = ConvertQuarterHourToMinutes(TradingEnd);
                ResetRuntimeState();
            }
            else if (State == State.DataLoaded)
            {
                rsi = RSI(RsiPeriod, 1);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < GetRequiredBarsCount() - 1)
                return;

            if (UseRsiFilter && CurrentBar < RsiPeriod + GetRequiredBarsCount() - 1)
                return;

            ExpirePendingEntryIfNeeded();

            bool insideTradingWindow = IsWithinTradingWindow();
            if (!insideTradingWindow)
            {
                CancelPendingEntries();
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            bool longPattern = HasLongPattern();
            bool shortPattern = HasShortPattern();

            if (!longPattern && !shortPattern)
                return;

            if (HasWorkingEntryOrders())
                CancelPendingEntries();

            if (longPattern)
            {
                if (InvertLogic)
                    SubmitShortEntry();
                else
                    SubmitLongEntry();

                return;
            }

            if (shortPattern)
            {
                if (InvertLogic)
                    SubmitLongEntry();
                else
                    SubmitShortEntry();
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            if (order.Name == LongSignalName)
                longEntryOrder = IsFinalOrderState(orderState) ? null : order;
            else if (order.Name == ShortSignalName)
                shortEntryOrder = IsFinalOrderState(orderState) ? null : order;

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
                pendingOrderCreationBar = -1;
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
                pendingOrderCreationBar = -1;
                return;
            }

            if (execution.Order.Name == ShortSignalName)
            {
                CancelPendingEntry(longEntryOrder);
                pendingOrderCreationBar = -1;
            }
        }

        private void SubmitLongEntry()
        {
            if (!EnableLongs)
                return;

            if (!PassesRsiFilter(true))
                return;

            double stopPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (EntryOffsetTicks * TickSize));
            int takeProfitTicks = GetTakeProfitTicks();

            SetStopLoss(LongSignalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(LongSignalName, CalculationMode.Ticks, takeProfitTicks);
            EnterLongStopMarket(0, true, EntryQuantity, stopPrice, LongSignalName);

            pendingOrderCreationBar = CurrentBar;
        }

        private void SubmitShortEntry()
        {
            if (!EnableShorts)
                return;

            if (!PassesRsiFilter(false))
                return;

            double stopPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] - (EntryOffsetTicks * TickSize));
            int takeProfitTicks = GetTakeProfitTicks();

            SetStopLoss(ShortSignalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(ShortSignalName, CalculationMode.Ticks, takeProfitTicks);
            EnterShortStopMarket(0, true, EntryQuantity, stopPrice, ShortSignalName);

            pendingOrderCreationBar = CurrentBar;
        }

        private bool HasLongPattern()
        {
            return HasSameDirectionBars(true, 0, BullishBarsCount)
                && HasSameDirectionBars(false, BullishBarsCount, BearishBarsCount);
        }

        private bool HasShortPattern()
        {
            return HasSameDirectionBars(false, 0, BearishBarsCount)
                && HasSameDirectionBars(true, BearishBarsCount, BullishBarsCount);
        }

        private bool HasSameDirectionBars(bool bullishBars, int startBarsAgo, int count)
        {
            for (int barsAgo = startBarsAgo; barsAgo < startBarsAgo + count; barsAgo++)
            {
                if (bullishBars && !IsBullishBar(barsAgo))
                    return false;

                if (!bullishBars && !IsBearishBar(barsAgo))
                    return false;
            }

            return true;
        }

        private bool IsBullishBar(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsBearishBar(int barsAgo)
        {
            return Close[barsAgo] < Open[barsAgo];
        }

        private bool PassesRsiFilter(bool longTrade)
        {
            if (!UseRsiFilter)
                return true;

            if (longTrade)
                return HasRsiAtOrBelowLevelInPattern()
                    && (!ConfirmRsiReCross || rsi[0] > RsiLongLevel);

            return HasRsiAtOrAboveLevelInPattern()
                && (!ConfirmRsiReCross || rsi[0] < RsiShortLevel);
        }

        private bool HasRsiAtOrBelowLevelInPattern()
        {
            for (int barsAgo = 0; barsAgo < GetRequiredBarsCount(); barsAgo++)
            {
                if (rsi[barsAgo] <= RsiLongLevel)
                    return true;
            }

            return false;
        }

        private bool HasRsiAtOrAboveLevelInPattern()
        {
            for (int barsAgo = 0; barsAgo < GetRequiredBarsCount(); barsAgo++)
            {
                if (rsi[barsAgo] >= RsiShortLevel)
                    return true;
            }

            return false;
        }

        private int GetTakeProfitTicks()
        {
            return Math.Max(1, (int)Math.Round(StopLossTicks * TakeProfitMultiplier, MidpointRounding.AwayFromZero));
        }

        private int GetRequiredBarsCount()
        {
            return BearishBarsCount + BullishBarsCount;
        }

        private bool IsWithinTradingWindow()
        {
            if (!UseTimeFilter || startMinutes == endMinutes)
                return true;

            int currentMinutes = (Time[0].Hour * 60) + Time[0].Minute;

            if (startMinutes < endMinutes)
                return currentMinutes >= startMinutes && currentMinutes <= endMinutes;

            return currentMinutes >= startMinutes || currentMinutes <= endMinutes;
        }

        private void ExpirePendingEntryIfNeeded()
        {
            if (BarsValid <= 0 || !HasWorkingEntryOrders() || pendingOrderCreationBar < 0)
                return;

            if (CurrentBar - pendingOrderCreationBar < BarsValid)
                return;

            CancelPendingEntries();
            pendingOrderCreationBar = -1;
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

        private bool IsFinalOrderState(OrderState orderState)
        {
            return orderState == OrderState.Cancelled
                || orderState == OrderState.Filled
                || orderState == OrderState.Rejected;
        }

        private void ValidateConfiguration()
        {
            if (EntryQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(EntryQuantity), "EntryQuantity debe ser mayor o igual que 1.");

            if (BearishBarsCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(BearishBarsCount), "BearishBarsCount debe ser mayor que 0.");

            if (BullishBarsCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(BullishBarsCount), "BullishBarsCount debe ser mayor que 0.");

            if (EntryOffsetTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(EntryOffsetTicks), "EntryOffsetTicks debe ser mayor que 0.");

            if (BarsValid < 0)
                throw new ArgumentOutOfRangeException(nameof(BarsValid), "BarsValid no puede ser negativo.");

            if (RsiPeriod <= 0)
                throw new ArgumentOutOfRangeException(nameof(RsiPeriod), "RsiPeriod debe ser mayor que 0.");

            if (RsiLongLevel < 0 || RsiLongLevel > 100)
                throw new ArgumentOutOfRangeException(nameof(RsiLongLevel), "RsiLongLevel debe estar entre 0 y 100.");

            if (RsiShortLevel < 0 || RsiShortLevel > 100)
                throw new ArgumentOutOfRangeException(nameof(RsiShortLevel), "RsiShortLevel debe estar entre 0 y 100.");

            if (StopLossTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(StopLossTicks), "StopLossTicks debe ser mayor que 0.");

            if (TakeProfitMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(TakeProfitMultiplier), "TakeProfitMultiplier debe ser mayor que 0.");

            if (!EnableLongs && !EnableShorts)
                throw new ArgumentException("Debes habilitar compras, ventas o ambas.");

            ValidateQuarterHourInput(TradingStart, nameof(TradingStart));
            ValidateQuarterHourInput(TradingEnd, nameof(TradingEnd));
        }

        private void ResetRuntimeState()
        {
            longEntryOrder = null;
            shortEntryOrder = null;
            pendingOrderCreationBar = -1;
        }

        private void ValidateQuarterHourInput(double value, string parameterName)
        {
            if (value < 0 || value > 23.75)
                throw new ArgumentOutOfRangeException(parameterName, parameterName + " debe estar entre 0.00 y 23.75.");

            double quarterValue = value * 4.0;
            if (Math.Abs(quarterValue - Math.Round(quarterValue)) > 0.0001)
            {
                throw new ArgumentException(
                    parameterName + " solo acepta incrementos de 0.25. Ejemplos validos: 9.00, 9.25, 9.50, 9.75.",
                    parameterName);
            }
        }

        private int ConvertQuarterHourToMinutes(double value)
        {
            return (int)Math.Round(value * 4.0) * 15;
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Cantidad", GroupName = "01. Orden", Order = 0)]
        public int EntryQuantity
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir compras", GroupName = "01. Orden", Order = 1)]
        public bool EnableLongs
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir ventas", GroupName = "01. Orden", Order = 2)]
        public bool EnableShorts
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Velas bajistas", Description = "Para compras son el primer bloque. Para ventas son el bloque reciente.", GroupName = "02. Patron", Order = 0)]
        public int BearishBarsCount
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Velas alcistas", Description = "Para compras son el bloque reciente. Para ventas son el primer bloque.", GroupName = "02. Patron", Order = 1)]
        public int BullishBarsCount
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Distancia stop entrada (ticks)", GroupName = "03. Entrada", Order = 0)]
        public int EntryOffsetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Validez orden (barras)", Description = "0 = la orden stop queda viva hasta ejecucion o nueva senal.", GroupName = "03. Entrada", Order = 1)]
        public int BarsValid
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Invertir logica", Description = "Si esta activo, el patron de compra lanza venta stop y el patron de venta lanza compra stop.", GroupName = "03. Entrada", Order = 2)]
        public bool InvertLogic
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario", GroupName = "04. Horario", Order = 0)]
        public bool UseTimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora inicio", Description = "Formato en cuartos de hora. Ejemplo: 9.50 = 9:30.", GroupName = "04. Horario", Order = 1)]
        public double TradingStart
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora fin", Description = "Formato en cuartos de hora. Ejemplo: 17.25 = 17:15.", GroupName = "04. Horario", Order = 2)]
        public double TradingEnd
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro RSI", GroupName = "05. RSI", Order = 0)]
        public bool UseRsiFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "RSI periodo", GroupName = "05. RSI", Order = 1)]
        public int RsiPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "RSI nivel compras", Description = "Compras buscan RSI <= este nivel en alguna vela del patron.", GroupName = "05. RSI", Order = 2)]
        public double RsiLongLevel
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "RSI nivel ventas", Description = "Ventas buscan RSI >= este nivel en alguna vela del patron.", GroupName = "05. RSI", Order = 3)]
        public double RsiShortLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Confirmar cruce RSI actual", Description = "Compras requieren RSI actual > nivel compras; ventas requieren RSI actual < nivel ventas.", GroupName = "05. RSI", Order = 4)]
        public bool ConfirmRsiReCross
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "06. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0000001, double.MaxValue)]
        [Display(Name = "Multiplicador take profit", Description = "Take profit en ticks = StopLossTicks * multiplicador.", GroupName = "06. Riesgo", Order = 1)]
        public double TakeProfitMultiplier
        { get; set; }
    }
}
