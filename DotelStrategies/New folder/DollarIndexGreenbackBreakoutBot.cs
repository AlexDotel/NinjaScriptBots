#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class DollarIndexGreenbackBreakoutBot : Strategy
    {
        private const string LongSignalName = "DX_Greenback_Long";
        private const string ShortSignalName = "DX_Greenback_Short";

        private TimeZoneInfo easternTimeZone;
        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int currentDayKey;
        private double rangeHigh;
        private double rangeLow;
        private bool hasRange;
        private bool ordersPlaced;
        private bool ordersCanceled;
        private bool positionFlattened;
        private bool tradedToday;
        private string lastStatusText;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Inicio rango ET (HHmm)", GroupName = "01. Horario ET", Order = 0)]
        public int RangeStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Fin rango ET (HHmm)", GroupName = "01. Horario ET", Order = 1)]
        public int RangeEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Colocar ordenes ET (HHmm)", GroupName = "01. Horario ET", Order = 2)]
        public int EntryHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Cancelar no ejecutadas ET (HHmm)", GroupName = "01. Horario ET", Order = 3)]
        public int CancelHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Cerrar posicion ET (HHmm)", GroupName = "01. Horario ET", Order = 4)]
        public int FlattenHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Operar historico", GroupName = "02. Ejecucion", Order = 0)]
        public bool TradeHistorical { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir compras", GroupName = "02. Ejecucion", Order = 1)]
        public bool AllowLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir ventas", GroupName = "02. Ejecucion", Order = 2)]
        public bool AllowShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Omitir lado ya roto", GroupName = "02. Ejecucion", Order = 3)]
        public bool SkipAlreadyBrokenSide { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Stop loss (ticks)", GroupName = "03. Riesgo", Order = 0)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Take profit (ticks)", GroupName = "03. Riesgo", Order = 1)]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos", GroupName = "03. Riesgo", Order = 2)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dibujar rango", GroupName = "04. Visual", Order = 0)]
        public bool DrawRangeLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar estado", GroupName = "04. Visual", Order = 1)]
        public bool ShowStatusOnChart { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DollarIndexGreenbackBreakoutBot";
                Description = "DX front month breakout: stop market en maximo/minimo del rango 03:00-08:30 ET, coloca 08:31 ET, cancela 09:30 ET y cierra 14:30 ET.";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = false;

                RangeStartHHmm = 300;
                RangeEndHHmm = 830;
                EntryHHmm = 831;
                CancelHHmm = 930;
                FlattenHHmm = 1430;

                TradeHistorical = false;
                AllowLongs = true;
                AllowShorts = true;
                SkipAlreadyBrokenSide = true;

                StopLossTicks = 15;
                TakeProfitTicks = 100;
                Quantity = 1;

                DrawRangeLevels = true;
                ShowStatusOnChart = true;
            }
            else if (State == State.Configure)
            {
                BarsRequiredToTrade = 1;
                SetStopLoss(LongSignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(LongSignalName, CalculationMode.Ticks, TakeProfitTicks);
                SetStopLoss(ShortSignalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(ShortSignalName, CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                ResetDailyState(-1);
                lastStatusText = string.Empty;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            DateTime easternTime = GetEasternTime(Time[0]);
            ResetDailyStateIfNeeded(easternTime);

            bool weekday = easternTime.DayOfWeek != DayOfWeek.Saturday && easternTime.DayOfWeek != DayOfWeek.Sunday;
            bool canTradeState = TradeHistorical || State == State.Realtime;

            if (weekday)
                UpdateRangeIfNeeded(easternTime);

            if (DrawRangeLevels && hasRange)
                DrawRangeReferenceLines();

            RenderStatusIfNeeded(easternTime, weekday, canTradeState);

            if (!weekday)
                return;

            if (ShouldFlatten(easternTime))
            {
                FlattenOpenPosition();
                CancelEntryOrders();
                positionFlattened = true;
                return;
            }

            if (!canTradeState)
                return;

            if (ShouldCancelUnfilledOrders(easternTime))
            {
                CancelEntryOrders();
                ordersCanceled = true;
                return;
            }

            if (ShouldPlaceEntryOrders(easternTime))
                PlaceEntryOrders();
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null)
                return;

            if (order.Name == LongSignalName)
                longEntryOrder = order;
            else if (order.Name == ShortSignalName)
                shortEntryOrder = order;

            if (IsTerminalOrderState(orderState))
            {
                if (order.Name == LongSignalName)
                    longEntryOrder = null;
                else if (order.Name == ShortSignalName)
                    shortEntryOrder = null;
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.Name == LongSignalName || execution.Order.Name == ShortSignalName)
            {
                tradedToday = true;

                if (execution.Order.Name == LongSignalName)
                    CancelEntryOrder(shortEntryOrder);
                else
                    CancelEntryOrder(longEntryOrder);
            }
        }

        private void UpdateRangeIfNeeded(DateTime easternTime)
        {
            int now = ToHHmmss(easternTime);
            int rangeStart = HHmmToIntTime(RangeStartHHmm);
            int rangeEnd = HHmmToIntTime(RangeEndHHmm);

            if (now < rangeStart || now > rangeEnd)
                return;

            if (!hasRange)
            {
                rangeHigh = High[0];
                rangeLow = Low[0];
                hasRange = true;
                return;
            }

            rangeHigh = Math.Max(rangeHigh, High[0]);
            rangeLow = Math.Min(rangeLow, Low[0]);
        }

        private bool ShouldPlaceEntryOrders(DateTime easternTime)
        {
            if (ordersPlaced || tradedToday || !hasRange)
                return false;

            if (Position.MarketPosition != MarketPosition.Flat)
                return false;

            int now = ToHHmmss(easternTime);
            return now >= HHmmToIntTime(EntryHHmm) && now < HHmmToIntTime(CancelHHmm);
        }

        private void PlaceEntryOrders()
        {
            double longStopPrice = Instrument.MasterInstrument.RoundToTickSize(rangeHigh);
            double shortStopPrice = Instrument.MasterInstrument.RoundToTickSize(rangeLow);
            bool submitLong = AllowLongs && (!SkipAlreadyBrokenSide || Close[0] < longStopPrice);
            bool submitShort = AllowShorts && (!SkipAlreadyBrokenSide || Close[0] > shortStopPrice);

            ordersPlaced = true;

            if (submitLong)
                longEntryOrder = EnterLongStopMarket(0, true, Quantity, longStopPrice, LongSignalName);

            if (submitShort)
                shortEntryOrder = EnterShortStopMarket(0, true, Quantity, shortStopPrice, ShortSignalName);
        }

        private bool ShouldCancelUnfilledOrders(DateTime easternTime)
        {
            if (ordersCanceled)
                return false;

            if (!ordersPlaced)
                return false;

            if (Position.MarketPosition != MarketPosition.Flat)
                return false;

            return ToHHmmss(easternTime) >= HHmmToIntTime(CancelHHmm);
        }

        private bool ShouldFlatten(DateTime easternTime)
        {
            if (positionFlattened)
                return false;

            return ToHHmmss(easternTime) >= HHmmToIntTime(FlattenHHmm);
        }

        private void FlattenOpenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("DX_Greenback_Flatten_Long", LongSignalName);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("DX_Greenback_Flatten_Short", ShortSignalName);
        }

        private void CancelEntryOrders()
        {
            CancelEntryOrder(longEntryOrder);
            CancelEntryOrder(shortEntryOrder);
        }

        private void CancelEntryOrder(Order order)
        {
            if (order == null || !IsWorkingOrderState(order.OrderState))
                return;

            CancelOrder(order);
        }

        private void ResetDailyStateIfNeeded(DateTime easternTime)
        {
            int dayKey = ToDay(easternTime);

            if (currentDayKey == dayKey)
                return;

            ResetDailyState(dayKey);
        }

        private void ResetDailyState(int dayKey)
        {
            currentDayKey = dayKey;
            longEntryOrder = null;
            shortEntryOrder = null;
            rangeHigh = 0;
            rangeLow = 0;
            hasRange = false;
            ordersPlaced = false;
            ordersCanceled = false;
            positionFlattened = false;
            tradedToday = false;
        }

        private DateTime GetEasternTime(DateTime barTime)
        {
            return TimeZoneInfo.ConvertTime(barTime, TimeZoneInfo.Local, easternTimeZone);
        }

        private bool IsWorkingOrderState(OrderState orderState)
        {
            return orderState == OrderState.Accepted
                || orderState == OrderState.Working
                || orderState == OrderState.PartFilled;
        }

        private bool IsTerminalOrderState(OrderState orderState)
        {
            return orderState == OrderState.Filled
                || orderState == OrderState.Cancelled
                || orderState == OrderState.Rejected;
        }

        private int ToHHmmss(DateTime time)
        {
            return time.Hour * 10000 + time.Minute * 100 + time.Second;
        }

        private int HHmmToIntTime(int hhmm)
        {
            int hours = Math.Max(0, Math.Min(23, hhmm / 100));
            int minutes = Math.Max(0, Math.Min(59, hhmm % 100));
            return hours * 10000 + minutes * 100;
        }

        private void DrawRangeReferenceLines()
        {
            Draw.HorizontalLine(this, "DX_Greenback_RangeHigh", rangeHigh, Brushes.LimeGreen);
            Draw.HorizontalLine(this, "DX_Greenback_RangeLow", rangeLow, Brushes.OrangeRed);
        }

        private void RenderStatusIfNeeded(DateTime easternTime, bool weekday, bool canTradeState)
        {
            if (!ShowStatusOnChart)
            {
                RemoveDrawObject("DX_Greenback_Status");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("DOLLAR INDEX GREENBACK BREAKOUT");
            builder.AppendLine(string.Format("Hora ET: {0:HH:mm:ss} | Dia habil: {1} | Estado: {2}",
                easternTime,
                weekday ? "SI" : "NO",
                BuildStateText(easternTime, weekday, canTradeState)));
            builder.AppendLine(string.Format("Rango {0:00}:{1:00}-{2:00}:{3:00} ET | High: {4} | Low: {5}",
                RangeStartHHmm / 100,
                RangeStartHHmm % 100,
                RangeEndHHmm / 100,
                RangeEndHHmm % 100,
                hasRange ? rangeHigh.ToString("N5") : "pendiente",
                hasRange ? rangeLow.ToString("N5") : "pendiente"));
            builder.AppendLine(string.Format("Entrada: {0:00}:{1:00} | Cancelar: {2:00}:{3:00} | Cerrar: {4:00}:{5:00}",
                EntryHHmm / 100,
                EntryHHmm % 100,
                CancelHHmm / 100,
                CancelHHmm % 100,
                FlattenHHmm / 100,
                FlattenHHmm % 100));
            builder.AppendLine(string.Format("SL/TP: {0}/{1} ticks | Contratos: {2}", StopLossTicks, TakeProfitTicks, Quantity));

            string statusText = builder.ToString();

            if (statusText == lastStatusText)
                return;

            Draw.TextFixed(this, "DX_Greenback_Status", statusText, TextPosition.TopLeft, Brushes.White,
                new SimpleFont("Consolas", 12), Brushes.Black, Brushes.DimGray, 70);
            lastStatusText = statusText;
        }

        private string BuildStateText(DateTime easternTime, bool weekday, bool canTradeState)
        {
            if (!weekday)
                return "Fin de semana";

            if (!canTradeState)
                return "Esperando realtime";

            if (positionFlattened)
                return "Cierre diario ejecutado";

            if (tradedToday)
                return Position.MarketPosition == MarketPosition.Flat ? "Operacion diaria finalizada" : "Posicion abierta";

            if (ordersCanceled)
                return "Ordenes canceladas";

            if (ordersPlaced)
                return "Ordenes stop trabajando";

            if (!hasRange)
                return ToHHmmss(easternTime) < HHmmToIntTime(RangeStartHHmm) ? "Esperando inicio de rango" : "Construyendo rango";

            if (ToHHmmss(easternTime) < HHmmToIntTime(EntryHHmm))
                return "Rango listo";

            return "Listo para colocar ordenes";
        }
    }
}
