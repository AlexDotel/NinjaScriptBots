#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ScheduledRangeBreakoutBot : Strategy
    {
        public enum ReferenceTimeZoneMode
        {
            Eastern,
            Central
        }

        private const string LongEntrySignalName = "RangeBreakoutLong";
        private const string ShortEntrySignalName = "RangeBreakoutShort";

        private TimeZoneInfo applicationTimeZone;
        private TimeZoneInfo referenceTimeZone;
        private int scheduledTimeValue;
        private DateTime currentReferenceDate;
        private bool referenceDateInitialized;
        private bool rangeDefinedToday;
        private bool tradeTriggeredToday;
        private double rangeHigh;
        private double rangeLow;
        private double longEntryStopPrice;
        private double shortEntryStopPrice;
        private double lastAppliedStopPrice;
        private Order longEntryOrder;
        private Order shortEntryOrder;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Captura el rango High/Low de la primera vela cerrada cuyo inicio cae en o despues de una hora configurada en US Eastern o US Central, y lanza breakout con ordenes stop, SL/TP y trailing.";
                Name = "ScheduledRangeBreakoutBot";
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
                ReferenceTimeZone = ReferenceTimeZoneMode.Eastern;
                TriggerTime = 16.00;

                EnableLongs = true;
                EnableShorts = true;
                LongEntryOffsetTicks = 0;
                ShortEntryOffsetTicks = 0;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;

                UseTrailingStop = true;
                TrailingTriggerTicks = 20;
                TrailingDistanceTicks = 12;
                TrailingStepTicks = 4;

                ShowRangeLines = true;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();

                scheduledTimeValue = ConvertQuarterHourToTimeValue(TriggerTime);
                applicationTimeZone = Core.Globals.GeneralOptions.TimeZoneInfo;
                referenceTimeZone = ResolveReferenceTimeZone(ReferenceTimeZone);

                ResetRuntimeState();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade - 1)
                return;

            DateTime closedBarOpenReferenceTime = GetClosedBarOpenTimeInReferenceZone();
            ResetDailyStateIfNeeded(closedBarOpenReferenceTime);

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                UpdateTrailingStop();
                return;
            }

            if (tradeTriggeredToday || rangeDefinedToday)
                return;

            if (!HasClosedTriggerBar(closedBarOpenReferenceTime))
                return;

            DefineRangeFromClosedBar();
            SubmitBreakoutOrders();
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            if (order.Name == LongEntrySignalName)
                longEntryOrder = IsEntryOrderFinalState(orderState) ? null : order;
            else if (order.Name == ShortEntrySignalName)
                shortEntryOrder = IsEntryOrderFinalState(orderState) ? null : order;

            if ((order.Name == LongEntrySignalName || order.Name == ShortEntrySignalName) && error != ErrorCode.NoError)
            {
                Print(string.Format(
                    "{0} | Error en orden {1}: {2} {3}",
                    Time[0],
                    order.Name,
                    error,
                    comment));
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
            {
                if (marketPosition == MarketPosition.Flat)
                    lastAppliedStopPrice = double.NaN;

                return;
            }

            if (execution.Order.Name == LongEntrySignalName)
            {
                tradeTriggeredToday = true;
                CancelPendingEntry(shortEntryOrder);
                lastAppliedStopPrice = Instrument.MasterInstrument.RoundToTickSize(price - (StopLossTicks * TickSize));

                Print(string.Format(
                    "{0} | Entrada LONG ejecutada a {1}. Rango [{2} - {3}]",
                    time,
                    price,
                    rangeLow,
                    rangeHigh));
                return;
            }

            if (execution.Order.Name == ShortEntrySignalName)
            {
                tradeTriggeredToday = true;
                CancelPendingEntry(longEntryOrder);
                lastAppliedStopPrice = Instrument.MasterInstrument.RoundToTickSize(price + (StopLossTicks * TickSize));

                Print(string.Format(
                    "{0} | Entrada SHORT ejecutada a {1}. Rango [{2} - {3}]",
                    time,
                    price,
                    rangeLow,
                    rangeHigh));
                return;
            }

            if (marketPosition == MarketPosition.Flat)
                lastAppliedStopPrice = double.NaN;
        }

        private void ValidateConfiguration()
        {
            if (EntryQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(EntryQuantity), "EntryQuantity debe ser mayor o igual que 1.");

            if (!EnableLongs && !EnableShorts)
                throw new ArgumentException("Debes habilitar compras, ventas o ambas.");

            ValidateQuarterHourInput(TriggerTime, nameof(TriggerTime));

            if (LongEntryOffsetTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(LongEntryOffsetTicks), "LongEntryOffsetTicks no puede ser negativo.");

            if (ShortEntryOffsetTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(ShortEntryOffsetTicks), "ShortEntryOffsetTicks no puede ser negativo.");

            if (StopLossTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(StopLossTicks), "StopLossTicks debe ser mayor que 0.");

            if (ProfitTargetTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(ProfitTargetTicks), "ProfitTargetTicks debe ser mayor que 0.");

            if (TrailingTriggerTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(TrailingTriggerTicks), "TrailingTriggerTicks no puede ser negativo.");

            if (TrailingDistanceTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(TrailingDistanceTicks), "TrailingDistanceTicks no puede ser negativo.");

            if (TrailingStepTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(TrailingStepTicks), "TrailingStepTicks no puede ser negativo.");
        }

        private void ResetRuntimeState()
        {
            currentReferenceDate = DateTime.MinValue;
            referenceDateInitialized = false;
            rangeDefinedToday = false;
            tradeTriggeredToday = false;
            rangeHigh = double.NaN;
            rangeLow = double.NaN;
            longEntryStopPrice = double.NaN;
            shortEntryStopPrice = double.NaN;
            lastAppliedStopPrice = double.NaN;
            longEntryOrder = null;
            shortEntryOrder = null;
        }

        private void ResetDailyStateIfNeeded(DateTime referenceBarOpenTime)
        {
            DateTime referenceDate = referenceBarOpenTime.Date;

            if (referenceDateInitialized && currentReferenceDate == referenceDate)
                return;

            currentReferenceDate = referenceDate;
            referenceDateInitialized = true;

            CancelPendingEntries();

            rangeDefinedToday = false;
            tradeTriggeredToday = false;
            rangeHigh = double.NaN;
            rangeLow = double.NaN;
            longEntryStopPrice = double.NaN;
            shortEntryStopPrice = double.NaN;

            if (Position.MarketPosition == MarketPosition.Flat)
                lastAppliedStopPrice = double.NaN;
        }

        private DateTime GetClosedBarOpenTimeInReferenceZone()
        {
            // NinjaTrader sella las barras con la hora de cierre; la barra anterior marca la hora de apertura de la barra recien cerrada.
            return ConvertBarTimeToReferenceZone(Time[1]);
        }

        private DateTime ConvertBarTimeToReferenceZone(DateTime barTime)
        {
            DateTime unspecifiedBarTime = DateTime.SpecifyKind(barTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTime(unspecifiedBarTime, applicationTimeZone, referenceTimeZone);
        }

        private TimeZoneInfo ResolveReferenceTimeZone(ReferenceTimeZoneMode mode)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    mode == ReferenceTimeZoneMode.Eastern
                        ? "Eastern Standard Time"
                        : "Central Standard Time");
            }
            catch (TimeZoneNotFoundException exception)
            {
                throw new ArgumentException("No se pudo resolver la zona horaria configurada.", exception);
            }
            catch (InvalidTimeZoneException exception)
            {
                throw new ArgumentException("La zona horaria configurada es invalida.", exception);
            }
        }

        private bool HasClosedTriggerBar(DateTime closedBarOpenReferenceTime)
        {
            if (ToTime(closedBarOpenReferenceTime) < scheduledTimeValue)
                return false;

            if (CurrentBar < 2)
                return true;

            DateTime previousClosedBarOpenReferenceTime = ConvertBarTimeToReferenceZone(Time[2]);
            if (previousClosedBarOpenReferenceTime.Date != closedBarOpenReferenceTime.Date)
                return true;

            return ToTime(previousClosedBarOpenReferenceTime) < scheduledTimeValue;
        }

        private void DefineRangeFromClosedBar()
        {
            rangeHigh = High[0];
            rangeLow = Low[0];
            longEntryStopPrice = Instrument.MasterInstrument.RoundToTickSize(rangeHigh + (LongEntryOffsetTicks * TickSize));
            shortEntryStopPrice = Instrument.MasterInstrument.RoundToTickSize(rangeLow - (ShortEntryOffsetTicks * TickSize));
            rangeDefinedToday = true;

            if (ShowRangeLines)
                DrawRangeLines();

            Print(string.Format(
                "{0} | Rango definido en {1} {2}. High={3} Low={4} BuyStop={5} SellStop={6}",
                Time[0],
                GetReferenceTimeZoneLabel(),
                FormatTimeValue(scheduledTimeValue),
                rangeHigh,
                rangeLow,
                longEntryStopPrice,
                shortEntryStopPrice));
        }

        private void DrawRangeLines()
        {
            string dateTag = currentReferenceDate.ToString("yyyyMMdd");
            Draw.HorizontalLine(this, "ScheduledRangeHigh_" + dateTag, rangeHigh, Brushes.SeaGreen);
            Draw.HorizontalLine(this, "ScheduledRangeLow_" + dateTag, rangeLow, Brushes.IndianRed);
        }

        private void SubmitBreakoutOrders()
        {
            if (EnableLongs)
            {
                SetStopLoss(LongEntrySignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(LongEntrySignalName, CalculationMode.Ticks, ProfitTargetTicks);
                EnterLongStopMarket(EntryQuantity, longEntryStopPrice, LongEntrySignalName);
            }

            if (EnableShorts)
            {
                SetStopLoss(ShortEntrySignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(ShortEntrySignalName, CalculationMode.Ticks, ProfitTargetTicks);
                EnterShortStopMarket(EntryQuantity, shortEntryStopPrice, ShortEntrySignalName);
            }
        }

        private void UpdateTrailingStop()
        {
            if (!UseTrailingStop || TrailingTriggerTicks <= 0 || TrailingDistanceTicks <= 0)
                return;

            double minImprovement = TrailingStepTicks * TickSize;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double triggerPrice = Position.AveragePrice + (TrailingTriggerTicks * TickSize);
                if (Close[0] < triggerPrice)
                    return;

                double candidateStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Close[0] - (TrailingDistanceTicks * TickSize));

                if (!double.IsNaN(lastAppliedStopPrice) && candidateStopPrice <= lastAppliedStopPrice + minImprovement)
                    return;

                if (candidateStopPrice >= Close[0])
                    return;

                SetStopLoss(LongEntrySignalName, CalculationMode.Price, candidateStopPrice, false);
                lastAppliedStopPrice = candidateStopPrice;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                double triggerPrice = Position.AveragePrice - (TrailingTriggerTicks * TickSize);
                if (Close[0] > triggerPrice)
                    return;

                double candidateStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Close[0] + (TrailingDistanceTicks * TickSize));

                if (!double.IsNaN(lastAppliedStopPrice) && candidateStopPrice >= lastAppliedStopPrice - minImprovement)
                    return;

                if (candidateStopPrice <= Close[0])
                    return;

                SetStopLoss(ShortEntrySignalName, CalculationMode.Price, candidateStopPrice, false);
                lastAppliedStopPrice = candidateStopPrice;
            }
        }

        private void CancelPendingEntries()
        {
            CancelPendingEntry(longEntryOrder);
            CancelPendingEntry(shortEntryOrder);
        }

        private void CancelPendingEntry(Order order)
        {
            if (!IsWorkingEntryOrder(order))
                return;

            CancelOrder(order);
        }

        private bool IsWorkingEntryOrder(Order order)
        {
            if (order == null)
                return false;

            return order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.PartFilled
                || order.OrderState == OrderState.Submitted
                || order.OrderState == OrderState.Working;
        }

        private bool IsEntryOrderFinalState(OrderState orderState)
        {
            return orderState == OrderState.Cancelled
                || orderState == OrderState.Filled
                || orderState == OrderState.Rejected;
        }

        private void ValidateQuarterHourInput(double value, string parameterName)
        {
            if (value < 0 || value > 23.75)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    parameterName + " debe estar entre 0.00 y 23.75.");
            }

            double quarterValue = value * 4.0;
            if (Math.Abs(quarterValue - Math.Round(quarterValue)) > 0.0001)
            {
                throw new ArgumentException(
                    parameterName + " solo acepta incrementos de 0.25. Ejemplos validos: 9.00, 9.25, 9.50, 9.75.",
                    parameterName);
            }
        }

        private int ConvertQuarterHourToTimeValue(double value)
        {
            int totalMinutes = (int)Math.Round(value * 60.0);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return (hours * 10000) + (minutes * 100);
        }

        private string FormatTimeValue(int timeValue)
        {
            int hours = timeValue / 10000;
            int minutes = (timeValue / 100) % 100;
            return string.Format("{0:00}:{1:00}", hours, minutes);
        }

        private string GetReferenceTimeZoneLabel()
        {
            return ReferenceTimeZone == ReferenceTimeZoneMode.Eastern ? "US Eastern" : "US Central";
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
        [Display(Name = "Zona horaria", Description = "La hora trigger se evaluara en US Eastern o US Central.", GroupName = "02. Horario", Order = 0)]
        public ReferenceTimeZoneMode ReferenceTimeZone
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora trigger", Description = "Formato decimal en cuartos de hora. Ejemplo: 16.25 = 16:15.", GroupName = "02. Horario", Order = 1)]
        public double TriggerTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset compra (ticks)", Description = "Distancia extra por encima del High del rango.", GroupName = "03. Breakout", Order = 0)]
        public int LongEntryOffsetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset venta (ticks)", Description = "Distancia extra por debajo del Low del rango.", GroupName = "03. Breakout", Order = 1)]
        public int ShortEntryOffsetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "04. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "04. Riesgo", Order = 1)]
        public int ProfitTargetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar trailing", GroupName = "05. Trailing", Order = 0)]
        public bool UseTrailingStop
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trigger trailing (ticks)", GroupName = "05. Trailing", Order = 1)]
        public int TrailingTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Distancia trailing (ticks)", GroupName = "05. Trailing", Order = 2)]
        public int TrailingDistanceTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Paso trailing (ticks)", Description = "0 = mover el stop en cada mejora util.", GroupName = "05. Trailing", Order = 3)]
        public int TrailingStepTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dibujar rango", GroupName = "06. Visual", Order = 0)]
        public bool ShowRangeLines
        { get; set; }
    }
}
