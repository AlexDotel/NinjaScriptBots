#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AnchorPriceAdvancedBotOptimizer : Strategy
    {
        private const int MaxSlots = 5;
        private const int DisabledMinutes = -1;
        private const double DisabledHourValue = -1.0;

        private ScheduleSlot[] scheduleSlots;
        private DateTime currentTradingDate;
        private bool tradingDateInitialized;
        private Dictionary<string, TrailingStopState> trailingStopStates;
        private bool currentSessionGapEvaluated;
        private bool currentSessionHasOpeningGap;
        private double currentSessionOpenPrice;
        private double previousSessionClosePrice;
        private double currentSessionGapTicks;
        private string currentSessionGapError;
        private ADX marketRegimeAdx;
        private EMA marketRegimeEma;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Version optimizer-friendly del Anchor multi-slot. Tolera solapes, normaliza horas a cuartos y resuelve conflictos sin abortar la optimizacion.";
                Name = "AnchorPriceAdvancedBotOptimizer";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = MaxSlots;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 2;
                DefaultQuantity = 1;

                UserQuantity = 1;
                EnableLongs = true;
                EnableShorts = true;

                CheckHour1 = 9.00;
                AnchorHour1 = 22.00;
                CheckHour2 = -1.0;
                AnchorHour2 = -1.0;
                CheckHour3 = -1.0;
                AnchorHour3 = -1.0;
                CheckHour4 = -1.0;
                AnchorHour4 = -1.0;
                CheckHour5 = -1.0;
                AnchorHour5 = -1.0;

                MinDistanceTicks = 10;
                RequireDailyOpeningGap = false;
                UseMarketRegimeFilter = false;
                MarketRegimeAdxPeriod = 14;
                MarketRegimeAdxTrendThreshold = 25;
                MarketRegimeEmaPeriod = 34;
                MarketRegimeSlopeLookbackBars = 5;
                MarketRegimeSlopeThresholdTicks = 6;
                StopLossTicks = 20;
                TakeProfitTicks = 20;
                UseTrailingStop = false;
                TrailingTriggerTicks = 10;
                TrailingDistanceTicks = 8;
                TrailingStepTicks = 2;
            }
            else if (State == State.Configure)
            {
                if (UserQuantity <= 0)
                    throw new ArgumentOutOfRangeException(nameof(UserQuantity), "UserQuantity debe ser mayor o igual que 1.");

                if (StopLossTicks <= 0)
                    throw new ArgumentOutOfRangeException(nameof(StopLossTicks), "StopLossTicks debe ser mayor que 0.");

                if (TakeProfitTicks <= 0)
                    throw new ArgumentOutOfRangeException(nameof(TakeProfitTicks), "TakeProfitTicks debe ser mayor que 0.");

                if (MinDistanceTicks < 0)
                    throw new ArgumentOutOfRangeException(nameof(MinDistanceTicks), "MinDistanceTicks no puede ser negativo.");

                if (MarketRegimeAdxPeriod <= 0)
                    throw new ArgumentOutOfRangeException(nameof(MarketRegimeAdxPeriod), "MarketRegimeAdxPeriod debe ser mayor que 0.");

                if (MarketRegimeAdxTrendThreshold < 0)
                    throw new ArgumentOutOfRangeException(nameof(MarketRegimeAdxTrendThreshold), "MarketRegimeAdxTrendThreshold no puede ser negativo.");

                if (MarketRegimeEmaPeriod <= 0)
                    throw new ArgumentOutOfRangeException(nameof(MarketRegimeEmaPeriod), "MarketRegimeEmaPeriod debe ser mayor que 0.");

                if (MarketRegimeSlopeLookbackBars <= 0)
                    throw new ArgumentOutOfRangeException(nameof(MarketRegimeSlopeLookbackBars), "MarketRegimeSlopeLookbackBars debe ser mayor que 0.");

                if (MarketRegimeSlopeThresholdTicks < 0)
                    throw new ArgumentOutOfRangeException(nameof(MarketRegimeSlopeThresholdTicks), "MarketRegimeSlopeThresholdTicks no puede ser negativo.");

                if (UseTrailingStop)
                {
                    if (TrailingTriggerTicks <= 0)
                        throw new ArgumentOutOfRangeException(nameof(TrailingTriggerTicks), "TrailingTriggerTicks debe ser mayor que 0.");

                    if (TrailingDistanceTicks <= 0)
                        throw new ArgumentOutOfRangeException(nameof(TrailingDistanceTicks), "TrailingDistanceTicks debe ser mayor que 0.");

                    if (TrailingStepTicks < 0)
                        throw new ArgumentOutOfRangeException(nameof(TrailingStepTicks), "TrailingStepTicks no puede ser negativo.");
                }

                trailingStopStates = new Dictionary<string, TrailingStopState>(StringComparer.Ordinal);
                scheduleSlots = BuildScheduleSlots();
            }
            else if (State == State.DataLoaded)
            {
                marketRegimeAdx = ADX(MarketRegimeAdxPeriod);
                marketRegimeEma = EMA(MarketRegimeEmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade - 1)
                return;

            if (scheduleSlots == null || scheduleSlots.Length == 0)
                return;

            ResetDailyStateIfNeeded();
            UpdateTrailingStops();

            List<TradeCandidate> triggeredCandidates = CollectTriggeredCandidates();
            if (triggeredCandidates.Count == 0)
                return;

            ExecuteTriggeredCandidates(triggeredCandidates);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (!UseTrailingStop || execution == null || execution.Order == null || execution.Order.OrderState != OrderState.Filled)
                return;

            Order order = execution.Order;
            string orderName = order.Name ?? string.Empty;

            if (IsAnchorEntryOrder(orderName, order.OrderAction))
            {
                RegisterFilledEntry(orderName, order.OrderAction, price, quantity);
                return;
            }

            string fromEntrySignal = order.FromEntrySignal ?? string.Empty;
            if (!string.IsNullOrEmpty(fromEntrySignal))
                RegisterFilledExit(fromEntrySignal, quantity);

            if (marketPosition == MarketPosition.Flat && trailingStopStates != null && trailingStopStates.Count > 0)
                trailingStopStates.Clear();
        }

        private ScheduleSlot[] BuildScheduleSlots()
        {
            ScheduleSlot[] slots = new[]
            {
                CreateSlotOrDisabled(1, CheckHour1, AnchorHour1),
                CreateSlotOrDisabled(2, CheckHour2, AnchorHour2),
                CreateSlotOrDisabled(3, CheckHour3, AnchorHour3),
                CreateSlotOrDisabled(4, CheckHour4, AnchorHour4),
                CreateSlotOrDisabled(5, CheckHour5, AnchorHour5)
            };

            Array.Sort(slots, CompareSlotsByCheckTimeThenSlotNumber);
            return slots;
        }

        private ScheduleSlot CreateSlotOrDisabled(int slotNumber, double checkHourDecimal, double anchorHourDecimal)
        {
            if (IsDisabledHour(checkHourDecimal) || IsDisabledHour(anchorHourDecimal))
                return ScheduleSlot.CreateDisabled(slotNumber);

            double normalizedCheckHour = NormalizeQuarterHourDecimal(checkHourDecimal);
            double normalizedAnchorHour = NormalizeQuarterHourDecimal(anchorHourDecimal);

            TimeSpan checkTime = ConvertQuarterHourDecimalToTimeSpan(normalizedCheckHour);
            TimeSpan anchorTime = ConvertQuarterHourDecimalToTimeSpan(normalizedAnchorHour);

            return new ScheduleSlot(slotNumber, normalizedCheckHour, normalizedAnchorHour, checkTime, anchorTime);
        }

        private static int CompareSlotsByCheckTimeThenSlotNumber(ScheduleSlot left, ScheduleSlot right)
        {
            int leftMinutes = left.IsEnabled ? left.CheckMinutes : int.MaxValue;
            int rightMinutes = right.IsEnabled ? right.CheckMinutes : int.MaxValue;

            int comparison = leftMinutes.CompareTo(rightMinutes);
            return comparison != 0 ? comparison : left.SlotNumber.CompareTo(right.SlotNumber);
        }

        private List<TradeCandidate> CollectTriggeredCandidates()
        {
            List<TradeCandidate> candidates = new List<TradeCandidate>();

            for (int slotIndex = 0; slotIndex < scheduleSlots.Length; slotIndex++)
            {
                ScheduleSlot slot = scheduleSlots[slotIndex];

                if (!HasSlotTriggeredThisBar(slot))
                    continue;

                slot.ProcessedToday = true;

                if (TryBuildTradeCandidate(slot, out TradeCandidate candidate))
                    candidates.Add(candidate);
            }

            return candidates;
        }

        private bool HasSlotTriggeredThisBar(ScheduleSlot slot)
        {
            if (!slot.IsEnabled || slot.ProcessedToday)
                return false;

            int currentMinutes = ConvertTimeToMinutes(Time[0]);
            if (currentMinutes < slot.CheckMinutes)
                return false;

            if (CurrentBar == 0 || Time[1].Date != Time[0].Date)
                return true;

            int previousMinutes = ConvertTimeToMinutes(Time[1]);
            return previousMinutes < slot.CheckMinutes;
        }

        private bool TryBuildTradeCandidate(ScheduleSlot slot, out TradeCandidate candidate)
        {
            candidate = null;

            double checkPrice = Close[0];

            if (!TryResolveAnchorPoint(Time[0], slot, out double anchorPrice, out DateTime resolvedAnchorTime, out string anchorError))
            {
                Print(string.Format(
                    "{0} | Slot {1} | No se pudo resolver AnchorPrice: {2}.",
                    Time[0],
                    slot.SlotNumber,
                    anchorError));
                return false;
            }

            double distanceTicks = Math.Abs(checkPrice - anchorPrice) / TickSize;
            if (distanceTicks < MinDistanceTicks)
            {
                Print(string.Format(
                    "{0} | Slot {1} | Distancia insuficiente. Anchor={2} Check={3} DistTicks={4:F2} MinTicks={5}.",
                    Time[0],
                    slot.SlotNumber,
                    anchorPrice,
                    checkPrice,
                    distanceTicks,
                    MinDistanceTicks));
                return false;
            }

            TradeDirection signalDirection = GetSignalDirection(anchorPrice, checkPrice);
            if (signalDirection == TradeDirection.None)
            {
                Print(string.Format(
                    "{0} | Slot {1} | AnchorPrice igual a CheckPrice. No se abre operacion.",
                    Time[0],
                    slot.SlotNumber));
                return false;
            }

            if (!IsDirectionEnabled(signalDirection))
            {
                Print(string.Format(
                    "{0} | Slot {1} | Senal {2} ignorada porque ese lado esta deshabilitado.",
                    Time[0],
                    slot.SlotNumber,
                    signalDirection));
                return false;
            }

            if (!PassesDailyOpeningGapFilter(slot.SlotNumber))
                return false;

            if (!PassesMarketRegimeFilter(slot.SlotNumber))
                return false;

            candidate = new TradeCandidate(slot, signalDirection, anchorPrice, checkPrice, distanceTicks, resolvedAnchorTime);
            return true;
        }

        private void ExecuteTriggeredCandidates(List<TradeCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return;

            TradeDirection effectiveDirection = GetEffectiveDirection();
            List<TradeCandidate> filteredCandidates = FilterCandidatesByDirection(candidates, effectiveDirection);

            if (filteredCandidates.Count == 0)
                return;

            filteredCandidates.Sort(CompareCandidates);

            foreach (TradeCandidate candidate in filteredCandidates)
            {
                string signalName = BuildEntrySignalName(candidate);
                PrepareProtectiveOrders(signalName);

                if (candidate.Direction == TradeDirection.Long)
                    EnterLong(UserQuantity, signalName);
                else
                    EnterShort(UserQuantity, signalName);

                Print(string.Format(
                    "{0} | Slot {1} | {2} | Anchor={3} Check={4} DistTicks={5:F2} Qty={6} (Anchor {7} {8}, bar {9:yyyy-MM-dd HH:mm:ss})",
                    Time[0],
                    candidate.Slot.SlotNumber,
                    candidate.Direction,
                    candidate.AnchorPrice,
                    candidate.CheckPrice,
                    candidate.DistanceTicks,
                    UserQuantity,
                    candidate.Slot.AnchorUsesPreviousDataDay ? "PREV_DATA_DAY" : "SAME_DAY",
                    FormatTime(candidate.Slot.AnchorTime),
                    candidate.ResolvedAnchorTime));
            }
        }

        private List<TradeCandidate> FilterCandidatesByDirection(List<TradeCandidate> candidates, TradeDirection effectiveDirection)
        {
            if (effectiveDirection != TradeDirection.None)
            {
                List<TradeCandidate> sameDirectionCandidates = new List<TradeCandidate>();

                foreach (TradeCandidate candidate in candidates)
                {
                    if (candidate.Direction == effectiveDirection)
                    {
                        sameDirectionCandidates.Add(candidate);
                        continue;
                    }

                    Print(string.Format(
                        "{0} | Slot {1} | Senal {2} omitida porque ya hay posicion o entrada dominante en direccion {3}.",
                        Time[0],
                        candidate.Slot.SlotNumber,
                        candidate.Direction,
                        effectiveDirection));
                }

                return sameDirectionCandidates;
            }

            bool hasLong = false;
            bool hasShort = false;

            foreach (TradeCandidate candidate in candidates)
            {
                hasLong |= candidate.Direction == TradeDirection.Long;
                hasShort |= candidate.Direction == TradeDirection.Short;
            }

            if (!hasLong || !hasShort)
                return new List<TradeCandidate>(candidates);

            TradeDirection selectedDirection = SelectWinningDirection(candidates);
            Print(string.Format(
                "{0} | Conflicto de direcciones en el mismo bar. Se prioriza {1} por mayor distancia efectiva.",
                Time[0],
                selectedDirection));

            List<TradeCandidate> selectedCandidates = new List<TradeCandidate>();

            foreach (TradeCandidate candidate in candidates)
            {
                if (candidate.Direction == selectedDirection)
                {
                    selectedCandidates.Add(candidate);
                    continue;
                }

                Print(string.Format(
                    "{0} | Slot {1} | Senal {2} descartada por conflicto en el mismo bar.",
                    Time[0],
                    candidate.Slot.SlotNumber,
                    candidate.Direction));
            }

            return selectedCandidates;
        }

        private TradeDirection SelectWinningDirection(List<TradeCandidate> candidates)
        {
            TradeCandidate winningCandidate = null;

            foreach (TradeCandidate candidate in candidates)
            {
                if (winningCandidate == null)
                {
                    winningCandidate = candidate;
                    continue;
                }

                if (candidate.DistanceTicks > winningCandidate.DistanceTicks)
                {
                    winningCandidate = candidate;
                    continue;
                }

                if (Math.Abs(candidate.DistanceTicks - winningCandidate.DistanceTicks) <= 1e-9
                    && candidate.Slot.SlotNumber < winningCandidate.Slot.SlotNumber)
                {
                    winningCandidate = candidate;
                }
            }

            return winningCandidate == null ? TradeDirection.None : winningCandidate.Direction;
        }

        private static int CompareCandidates(TradeCandidate left, TradeCandidate right)
        {
            int comparison = left.Slot.CheckMinutes.CompareTo(right.Slot.CheckMinutes);
            return comparison != 0 ? comparison : left.Slot.SlotNumber.CompareTo(right.Slot.SlotNumber);
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, TakeProfitTicks);
        }

        private void UpdateTrailingStops()
        {
            if (!UseTrailingStop || trailingStopStates == null || trailingStopStates.Count == 0)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                trailingStopStates.Clear();
                return;
            }

            double minImprovement = TrailingStepTicks * TickSize;
            List<string> signalNames = new List<string>(trailingStopStates.Keys);

            for (int signalIndex = 0; signalIndex < signalNames.Count; signalIndex++)
            {
                string signalName = signalNames[signalIndex];
                TrailingStopState state = trailingStopStates[signalName];

                if (state.Quantity <= 0)
                {
                    trailingStopStates.Remove(signalName);
                    continue;
                }

                if (state.Direction == TradeDirection.Long)
                {
                    double triggerPrice = state.EntryPrice + (TrailingTriggerTicks * TickSize);
                    if (Close[0] < triggerPrice)
                        continue;

                    double candidateStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                        Close[0] - (TrailingDistanceTicks * TickSize));

                    if (candidateStopPrice >= Close[0])
                        continue;

                    if (!double.IsNaN(state.LastAppliedStopPrice)
                        && candidateStopPrice <= state.LastAppliedStopPrice + minImprovement)
                    {
                        continue;
                    }

                    SetStopLoss(signalName, CalculationMode.Price, candidateStopPrice, false);
                    state.LastAppliedStopPrice = candidateStopPrice;
                    continue;
                }

                double shortTriggerPrice = state.EntryPrice - (TrailingTriggerTicks * TickSize);
                if (Close[0] > shortTriggerPrice)
                    continue;

                double shortCandidateStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                    Close[0] + (TrailingDistanceTicks * TickSize));

                if (shortCandidateStopPrice <= Close[0])
                    continue;

                if (!double.IsNaN(state.LastAppliedStopPrice)
                    && shortCandidateStopPrice >= state.LastAppliedStopPrice - minImprovement)
                {
                    continue;
                }

                SetStopLoss(signalName, CalculationMode.Price, shortCandidateStopPrice, false);
                state.LastAppliedStopPrice = shortCandidateStopPrice;
            }
        }

        private TradeDirection GetEffectiveDirection()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return TradeDirection.Long;

            if (Position.MarketPosition == MarketPosition.Short)
                return TradeDirection.Short;

            return TradeDirection.None;
        }

        private bool IsDirectionEnabled(TradeDirection direction)
        {
            if (direction == TradeDirection.Long)
                return EnableLongs;

            if (direction == TradeDirection.Short)
                return EnableShorts;

            return false;
        }

        private TradeDirection GetSignalDirection(double anchorPrice, double checkPrice)
        {
            if (anchorPrice > checkPrice)
                return TradeDirection.Long;

            if (anchorPrice < checkPrice)
                return TradeDirection.Short;

            return TradeDirection.None;
        }

        private string BuildEntrySignalName(TradeCandidate candidate)
        {
            string directionName = candidate.Direction == TradeDirection.Long ? "Long" : "Short";
            return string.Format("AnchorOpt{0}_S{1}_{2}", directionName, candidate.Slot.SlotNumber, ToDay(Time[0]));
        }

        private void ResetDailyStateIfNeeded()
        {
            DateTime barDate = Time[0].Date;

            if (tradingDateInitialized && currentTradingDate == barDate)
                return;

            currentTradingDate = barDate;
            tradingDateInitialized = true;

            for (int slotIndex = 0; slotIndex < scheduleSlots.Length; slotIndex++)
                scheduleSlots[slotIndex].ProcessedToday = false;

            RefreshCurrentSessionGapState();
        }

        private bool PassesDailyOpeningGapFilter(int slotNumber)
        {
            if (!RequireDailyOpeningGap)
                return true;

            if (!currentSessionGapEvaluated)
            {
                Print(string.Format(
                    "{0} | Slot {1} | Entrada omitida: no se pudo evaluar el gap diario ({2}).",
                    Time[0],
                    slotNumber,
                    string.IsNullOrEmpty(currentSessionGapError) ? "datos insuficientes" : currentSessionGapError));
                return false;
            }

            if (currentSessionHasOpeningGap)
                return true;

            Print(string.Format(
                "{0} | Slot {1} | Entrada omitida por filtro gap diario. OpenDia={2} PrevClose={3} GapTicks={4:F2}.",
                Time[0],
                slotNumber,
                currentSessionOpenPrice,
                previousSessionClosePrice,
                currentSessionGapTicks));
            return false;
        }

        private bool PassesMarketRegimeFilter(int slotNumber)
        {
            if (!UseMarketRegimeFilter)
                return true;

            if (!HasEnoughBarsForMarketRegime() || marketRegimeAdx == null || marketRegimeEma == null)
            {
                Print(string.Format(
                    "{0} | Slot {1} | Entrada omitida: filtro de contexto sin barras suficientes.",
                    Time[0],
                    slotNumber));
                return false;
            }

            double adxValue = marketRegimeAdx[0];
            double emaSlopeTicks = GetCurrentMarketRegimeSlopeTicks();

            if (adxValue < MarketRegimeAdxTrendThreshold)
                return true;

            if (emaSlopeTicks < MarketRegimeSlopeThresholdTicks)
                return true;

            Print(string.Format(
                "{0} | Slot {1} | Entrada omitida por filtro contexto. Mercado tendencial. ADX={2:F2} SlopeTicks={3:F2}.",
                Time[0],
                slotNumber,
                adxValue,
                emaSlopeTicks));
            return false;
        }

        private void RefreshCurrentSessionGapState()
        {
            currentSessionGapEvaluated = TryResolveSessionOpeningGap(
                currentTradingDate,
                out currentSessionOpenPrice,
                out previousSessionClosePrice,
                out currentSessionGapTicks,
                out currentSessionGapError);

            currentSessionHasOpeningGap = currentSessionGapEvaluated && currentSessionGapTicks >= 1.0 - 1e-9;
        }

        private bool HasEnoughBarsForMarketRegime()
        {
            int requiredBarIndex = Math.Max(
                MarketRegimeAdxPeriod,
                MarketRegimeEmaPeriod + MarketRegimeSlopeLookbackBars - 1);

            return CurrentBar >= requiredBarIndex;
        }

        private double GetCurrentMarketRegimeSlopeTicks()
        {
            return Math.Abs(marketRegimeEma[0] - marketRegimeEma[MarketRegimeSlopeLookbackBars]) / TickSize;
        }

        private bool TryResolveSessionOpeningGap(DateTime tradingDay, out double sessionOpenPrice, out double previousDayClosePrice,
            out double gapTicks, out string error)
        {
            sessionOpenPrice = double.NaN;
            previousDayClosePrice = double.NaN;
            gapTicks = 0.0;
            error = null;

            int firstBarOfDayBarsAgo = FindFirstBarOfDay(tradingDay);
            if (firstBarOfDayBarsAgo < 0)
            {
                error = string.Format("No se encontro el inicio del dia {0:yyyy-MM-dd}.", tradingDay);
                return false;
            }

            int previousDayCloseBarsAgo = firstBarOfDayBarsAgo + 1;
            if (previousDayCloseBarsAgo > CurrentBar)
            {
                error = string.Format("No hay cierre previo disponible para {0:yyyy-MM-dd}.", tradingDay);
                return false;
            }

            sessionOpenPrice = Open[firstBarOfDayBarsAgo];
            previousDayClosePrice = Close[previousDayCloseBarsAgo];
            gapTicks = Math.Abs(sessionOpenPrice - previousDayClosePrice) / TickSize;
            return true;
        }

        private int FindFirstBarOfDay(DateTime tradingDay)
        {
            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                if (Time[barsAgo].Date != tradingDay)
                    return barsAgo - 1;
            }

            if (CurrentBar >= 0 && Time[CurrentBar].Date == tradingDay)
                return CurrentBar;

            return -1;
        }

        private bool IsAnchorEntryOrder(string orderName, OrderAction orderAction)
        {
            if (string.IsNullOrEmpty(orderName))
                return false;

            if (orderAction != OrderAction.Buy && orderAction != OrderAction.SellShort)
                return false;

            return orderName.StartsWith("Anchor", StringComparison.Ordinal);
        }

        private void RegisterFilledEntry(string signalName, OrderAction orderAction, double fillPrice, int fillQuantity)
        {
            if (trailingStopStates == null)
                trailingStopStates = new Dictionary<string, TrailingStopState>(StringComparer.Ordinal);

            TradeDirection direction = orderAction == OrderAction.Buy ? TradeDirection.Long : TradeDirection.Short;
            double initialStopPrice = GetInitialStopPrice(fillPrice, direction);

            if (!trailingStopStates.TryGetValue(signalName, out TrailingStopState state))
            {
                trailingStopStates[signalName] = new TrailingStopState(direction, fillPrice, fillQuantity, initialStopPrice);
                return;
            }

            state.RegisterEntryFill(fillPrice, fillQuantity, initialStopPrice);
        }

        private void RegisterFilledExit(string signalName, int fillQuantity)
        {
            if (trailingStopStates == null || !trailingStopStates.TryGetValue(signalName, out TrailingStopState state))
                return;

            if (state.RegisterExitFill(fillQuantity))
                trailingStopStates.Remove(signalName);
        }

        private double GetInitialStopPrice(double entryPrice, TradeDirection direction)
        {
            double stopPrice = direction == TradeDirection.Long
                ? entryPrice - (StopLossTicks * TickSize)
                : entryPrice + (StopLossTicks * TickSize);

            return Instrument.MasterInstrument.RoundToTickSize(stopPrice);
        }

        private bool TryResolveAnchorPoint(DateTime checkBarTime, ScheduleSlot slot, out double resolvedAnchorPrice, out DateTime resolvedAnchorTime, out string error)
        {
            resolvedAnchorPrice = double.NaN;
            resolvedAnchorTime = DateTime.MinValue;
            error = null;

            DateTime checkDay = checkBarTime.Date;
            DateTime anchorDay = checkDay;

            if (slot.AnchorUsesPreviousDataDay)
            {
                if (!TryGetPreviousDayWithData(checkDay, out anchorDay, out string previousDayError))
                {
                    error = previousDayError;
                    return false;
                }
            }

            DateTime targetAnchorDateTime = anchorDay.Add(slot.AnchorTime);
            int barsAgo = FindFirstBarAtOrAfter(targetAnchorDateTime);

            if (barsAgo < 0)
            {
                error = string.Format("No hay bar disponible en o despues de {0:yyyy-MM-dd HH:mm:ss}.", targetAnchorDateTime);
                return false;
            }

            if (Time[barsAgo].Date != anchorDay)
            {
                error = string.Format(
                    "El bar resuelto cae en {0:yyyy-MM-dd HH:mm:ss} y no en el dia esperado {1:yyyy-MM-dd}.",
                    Time[barsAgo],
                    anchorDay);
                return false;
            }

            resolvedAnchorPrice = Close[barsAgo];
            resolvedAnchorTime = Time[barsAgo];
            return true;
        }

        private bool TryGetPreviousDayWithData(DateTime referenceDay, out DateTime previousDayWithData, out string error)
        {
            previousDayWithData = DateTime.MinValue;
            error = null;

            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                DateTime barDate = Time[barsAgo].Date;
                if (barDate < referenceDay)
                {
                    previousDayWithData = barDate;
                    return true;
                }
            }

            error = string.Format("No se encontro un dia con datos anterior a {0:yyyy-MM-dd}.", referenceDay);
            return false;
        }

        private int FindFirstBarAtOrAfter(DateTime targetDateTime)
        {
            if (Time[0] < targetDateTime)
                return -1;

            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                if (Time[barsAgo] < targetDateTime)
                {
                    int previousBarsAgo = barsAgo - 1;
                    return previousBarsAgo >= 0 ? previousBarsAgo : -1;
                }
            }

            return CurrentBar;
        }

        private double NormalizeQuarterHourDecimal(double hourDecimal)
        {
            double clampedValue = Math.Max(0.0, Math.Min(23.75, hourDecimal));
            double normalizedValue = Math.Round(clampedValue * 4.0, MidpointRounding.AwayFromZero) / 4.0;
            return Math.Max(0.0, Math.Min(23.75, normalizedValue));
        }

        private TimeSpan ConvertQuarterHourDecimalToTimeSpan(double hourDecimal)
        {
            int totalMinutes = (int)Math.Round(hourDecimal * 60.0);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return new TimeSpan(hours, minutes, 0);
        }

        private bool IsDisabledHour(double value)
        {
            return Math.Abs(value - DisabledHourValue) <= 0.0001;
        }

        private int ConvertTimeToMinutes(DateTime time)
        {
            return (time.Hour * 60) + time.Minute;
        }

        private string FormatTime(TimeSpan time)
        {
            return string.Format("{0:00}:{1:00}", time.Hours, time.Minutes);
        }

        private int ToDay(DateTime time)
        {
            return (time.Year * 10000) + (time.Month * 100) + time.Day;
        }

        private enum TradeDirection
        {
            None,
            Long,
            Short
        }

        private sealed class ScheduleSlot
        {
            public ScheduleSlot(int slotNumber, double checkHourDecimal, double anchorHourDecimal, TimeSpan checkTime, TimeSpan anchorTime)
            {
                SlotNumber = slotNumber;
                CheckHourDecimal = checkHourDecimal;
                AnchorHourDecimal = anchorHourDecimal;
                CheckTime = checkTime;
                AnchorTime = anchorTime;
                CheckMinutes = (checkTime.Hours * 60) + checkTime.Minutes;
                AnchorUsesPreviousDataDay = anchorTime > checkTime;
                IsEnabled = true;
                ProcessedToday = false;
            }

            private ScheduleSlot(int slotNumber)
            {
                SlotNumber = slotNumber;
                CheckHourDecimal = DisabledHourValue;
                AnchorHourDecimal = DisabledHourValue;
                CheckTime = TimeSpan.Zero;
                AnchorTime = TimeSpan.Zero;
                CheckMinutes = DisabledMinutes;
                AnchorUsesPreviousDataDay = false;
                IsEnabled = false;
                ProcessedToday = false;
            }

            public int SlotNumber { get; private set; }
            public bool IsEnabled { get; private set; }
            public bool ProcessedToday { get; set; }
            public double CheckHourDecimal { get; private set; }
            public double AnchorHourDecimal { get; private set; }
            public TimeSpan CheckTime { get; private set; }
            public TimeSpan AnchorTime { get; private set; }
            public int CheckMinutes { get; private set; }
            public bool AnchorUsesPreviousDataDay { get; private set; }

            public static ScheduleSlot CreateDisabled(int slotNumber)
            {
                return new ScheduleSlot(slotNumber);
            }
        }

        private sealed class TradeCandidate
        {
            public TradeCandidate(ScheduleSlot slot, TradeDirection direction, double anchorPrice, double checkPrice, double distanceTicks, DateTime resolvedAnchorTime)
            {
                Slot = slot;
                Direction = direction;
                AnchorPrice = anchorPrice;
                CheckPrice = checkPrice;
                DistanceTicks = distanceTicks;
                ResolvedAnchorTime = resolvedAnchorTime;
            }

            public ScheduleSlot Slot { get; private set; }
            public TradeDirection Direction { get; private set; }
            public double AnchorPrice { get; private set; }
            public double CheckPrice { get; private set; }
            public double DistanceTicks { get; private set; }
            public DateTime ResolvedAnchorTime { get; private set; }
        }

        private sealed class TrailingStopState
        {
            public TrailingStopState(TradeDirection direction, double entryPrice, int quantity, double lastAppliedStopPrice)
            {
                Direction = direction;
                EntryPrice = entryPrice;
                Quantity = quantity;
                LastAppliedStopPrice = lastAppliedStopPrice;
            }

            public TradeDirection Direction { get; private set; }
            public double EntryPrice { get; private set; }
            public int Quantity { get; private set; }
            public double LastAppliedStopPrice { get; set; }

            public void RegisterEntryFill(double fillPrice, int fillQuantity, double initialStopPrice)
            {
                if (fillQuantity <= 0)
                    return;

                if (Quantity <= 0)
                {
                    EntryPrice = fillPrice;
                    Quantity = fillQuantity;
                    LastAppliedStopPrice = initialStopPrice;
                    return;
                }

                double totalEntryValue = (EntryPrice * Quantity) + (fillPrice * fillQuantity);
                Quantity += fillQuantity;
                EntryPrice = totalEntryValue / Quantity;
                LastAppliedStopPrice = initialStopPrice;
            }

            public bool RegisterExitFill(int fillQuantity)
            {
                if (fillQuantity <= 0)
                    return Quantity <= 0;

                Quantity = Math.Max(0, Quantity - fillQuantity);
                return Quantity == 0;
            }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Cantidad", GroupName = "01. Orden", Order = 0)]
        public int UserQuantity
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
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora check 1", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 0)]
        public double CheckHour1
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora anchor 1", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 1)]
        public double AnchorHour1
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora check 2", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 2)]
        public double CheckHour2
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora anchor 2", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 3)]
        public double AnchorHour2
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora check 3", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 4)]
        public double CheckHour3
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora anchor 3", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 5)]
        public double AnchorHour3
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora check 4", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 6)]
        public double CheckHour4
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora anchor 4", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 7)]
        public double AnchorHour4
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora check 5", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 8)]
        public double CheckHour5
        { get; set; }

        [NinjaScriptProperty]
        [Range(-1.0, 23.75)]
        [Display(Name = "Hora anchor 5", Description = "Formato decimal en cuartos de hora. Usa -1 para desactivar el slot.", GroupName = "02. Horarios", Order = 9)]
        public double AnchorHour5
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Distancia minima (ticks)", GroupName = "03. Reglas", Order = 0)]
        public int MinDistanceTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Requerir gap apertura diario", GroupName = "03. Reglas", Order = 1)]
        public bool RequireDailyOpeningGap
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtrar solo reversion media", GroupName = "03. Reglas", Order = 2)]
        public bool UseMarketRegimeFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Periodo ADX contexto", GroupName = "03. Reglas", Order = 3)]
        public int MarketRegimeAdxPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Umbral ADX tendencia", GroupName = "03. Reglas", Order = 4)]
        public double MarketRegimeAdxTrendThreshold
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Periodo EMA contexto", GroupName = "03. Reglas", Order = 5)]
        public int MarketRegimeEmaPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Lookback pendiente EMA", GroupName = "03. Reglas", Order = 6)]
        public int MarketRegimeSlopeLookbackBars
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Pendiente minima EMA (ticks)", GroupName = "03. Reglas", Order = 7)]
        public int MarketRegimeSlopeThresholdTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "04. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "04. Riesgo", Order = 1)]
        public int TakeProfitTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar trailing", GroupName = "05. Trailing", Order = 0)]
        public bool UseTrailingStop
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trigger trailing (ticks)", GroupName = "05. Trailing", Order = 1)]
        public int TrailingTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Distancia trailing (ticks)", GroupName = "05. Trailing", Order = 2)]
        public int TrailingDistanceTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Paso trailing (ticks)", Description = "0 = mover el stop en cada mejora util.", GroupName = "05. Trailing", Order = 3)]
        public int TrailingStepTicks
        { get; set; }
    }
}
