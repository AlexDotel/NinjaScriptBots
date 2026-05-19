#region Using declarations
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion


namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class AnchorRelease : Strategy
    {
        private const int MaxSlots = 5;
        private const int DisabledMinutes = -1;
        private const double DisabledHourValue = -1.0;

        private ScheduleSlot[] scheduleSlots;
        private DateTime currentTradingDate;
        private bool tradingDateInitialized;
        private DateTime currentAnchorSegmentStartTime;
        private double currentAnchorSegmentPrice = double.NaN;
        private string currentAnchorSegmentTag = string.Empty;
        private int anchorSegmentCounter;
        private int lastAnchorVisualDayKey = -1;
        private int lastAnchorVisualSegmentCount;

        // Control de licencia: ajusta estos valores por cliente/version.
        private static readonly DateTime ExpirationDate = new DateTime(2026, 6, 20);
        private static readonly string[] AllowedMachineIds = new[]
        {
			//"64B1CDD3679276C4900736F898D11557", Mi machine ID
		    "79FDF03F1707DE25D5F6E1A1641706DB", // Angel Rojas LIFETIME
		    "19ECE3C188BBFE002FEB1F3C57377DF9", // Christian LIFETIME
		    "5E71203498EC6EF8885129E3162954FD", // Ergus LIFETIME
		    "C737D72B4DBE5D93A6DDD45636F978E6", // Johan LIFETIME
		    "CC885E6F29B579357B7F15255498E365", // Novas LIFETIME
			
		    "38E81B0FC6ED3C2536E7C164321724D9", // Zidam Pago Mayo
		    "C829A31CC9FBE4B2F4BA3F2CF80C9A77", // Rao Pago Mayo
		    "62E38127062BFA38740473E0EBA88C56", // Jason Stokes Free
		    //"6E5FF89447E468D19A2F3B6805D15ECD", // Gumbleberry Abril
		    "760029F55C1F65D197DB8EF27C8D53D5", // Ain Mayo
			"3FFD373A1DFD9947E359D74B350F105A", // Antonio Fernandez Skool 
		    //"A54CF05AA1743989C525B7D280B72FF7", // Armen Out
		    //"ECF47AD1E9D6BAF78E49F27ECF3DE84E",
		    //"1259742D2056BAB913D5677886F792F2"
            // "OTRO_MACHINE_ID"
        };
        private bool expirationPrinted;
        private bool invalidLicensePrinted;
        private double panelAnchorPrice = double.NaN;
        private double panelCheckPrice = double.NaN;
        private double panelDistanceTicks = double.NaN;
        private bool? panelMinDistanceMet;
        private string tradeOutcomeText = "Sin evaluar";
        private string lastSignalText = "Sin señal";
        private string licenseStatusText = "ACTIVA";
        private int panelSlotNumber;
        private readonly Dictionary<string, ActiveTradeState> activeTrades = new Dictionary<string, ActiveTradeState>(StringComparer.OrdinalIgnoreCase);
        private double currentSessionGapTicks = double.NaN;

        private Chart chartWindow;
        private ChartTrader chartTrader;
        private Grid chartTraderGrid;
        private Border statusPanelBorder;
        private StackPanel statusPanelStack;

        private TextBlock tbBotName;
        private TextBlock tbLicense;
        private TextBlock tbExpiry;
        private Border sep1;
        private TextBlock tbAnchor;
        private TextBlock tbCheck;
        private TextBlock tbDistance;
        private TextBlock tbMinTicks;
        private Border sep2;
        private TextBlock tbSignal;
        private TextBlock tbResult;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Anchor multi-slot con filtro de gap de apertura, breakeven y trailing stop por entrada.";
                Name = "AnchorRelease";
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
                RequireDirectionalOpenGap = true;
                MinOpenGapTicks = 1;
                StopLossTicks = 20;
                TakeProfitTicks = 20;
                UseBreakEven = true;
                BreakEvenTriggerTicks = 6;
                BreakEvenPlusTicks = 1;
                UseTrailingStop = true;
                TrailingTriggerTicks = 10;
                TrailingDistanceTicks = 8;
                TrailingStepTicks = 2;

                PanelFontSize = 14;
                PanelFontFamily = "Arial";
                PanelTextColor = Brushes.White;
                PanelBackgroundColor = Brushes.Black;
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

                if (UseBreakEven && BreakEvenTriggerTicks <= 0)
                    throw new ArgumentOutOfRangeException(nameof(BreakEvenTriggerTicks), "BreakEvenTriggerTicks debe ser mayor que 0.");

                if (BreakEvenPlusTicks < 0)
                    throw new ArgumentOutOfRangeException(nameof(BreakEvenPlusTicks), "BreakEvenPlusTicks no puede ser negativo.");

                if (UseTrailingStop && TrailingTriggerTicks <= 0)
                    throw new ArgumentOutOfRangeException(nameof(TrailingTriggerTicks), "TrailingTriggerTicks debe ser mayor que 0.");

                if (UseTrailingStop && TrailingDistanceTicks <= 0)
                    throw new ArgumentOutOfRangeException(nameof(TrailingDistanceTicks), "TrailingDistanceTicks debe ser mayor que 0.");

                if (UseTrailingStop && TrailingStepTicks <= 0)
                    throw new ArgumentOutOfRangeException(nameof(TrailingStepTicks), "TrailingStepTicks debe ser mayor que 0.");

                scheduleSlots = BuildScheduleSlots();
            }
            else if (State == State.Historical)
            {
                TryCreatePanel();
            }
            else if (State == State.Terminated)
            {
                RemoveStatusPanel();
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

            TryCreatePanel();

            bool enforceLicense = State == State.Realtime;
            if (enforceLicense)
            {
                if (!IsMachineIdValid())
                {
                    licenseStatusText = "INVALIDA";
                    RefreshStatusPanel();

                    if (!invalidLicensePrinted)
                    {
                        invalidLicensePrinted = true;
                        Print(string.Format(
                            "{0} | Invalid license. Current Machine ID: {1} | This machine is not in the list of authorized machines.",
                            Time[0],
                            GetCurrentMachineId()));
                    }

                    return;
                }

                invalidLicensePrinted = false;
                licenseStatusText = "ACTIVA";

                if (Time[0].Date > ExpirationDate)
                {
                    licenseStatusText = "EXPIRADA";
                    RefreshStatusPanel();

                    if (!expirationPrinted)
                    {
                        expirationPrinted = true;
                        Print(string.Format(
                            "{0} | Strategy expired. Valid until {1:dd/MM/yyyy} (inclusive). Contact the developer: @isdotel on Discord",
                            Time[0],
                            ExpirationDate));
                    }

                    return;
                }
            }
            else
            {
                invalidLicensePrinted = false;
                expirationPrinted = false;
                licenseStatusText = "BACKTEST";
            }

            UpdateSessionGapState();
            ManageActiveTrades();
            ResetDailyStateIfNeeded();
            UpdateCurrentAnchorVisual(Time[0]);
            UpdateCurrentAnchorPanelState(Time[0]);
            RefreshStatusPanel();
            RefreshActiveAnchorSegment();

            TradeDirection effectiveDirection = GetEffectiveDirection();

            for (int slotIndex = 0; slotIndex < scheduleSlots.Length; slotIndex++)
            {
                ScheduleSlot slot = scheduleSlots[slotIndex];

                if (!HasSlotTriggeredThisBar(slot))
                    continue;

                slot.ProcessedToday = true;
                TryExecuteSlot(slot, ref effectiveDirection);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            string orderName = execution.Order.Name ?? string.Empty;

            if (execution.Order.OrderState == OrderState.Filled)
            {
                if (activeTrades.TryGetValue(orderName, out ActiveTradeState entryTrade))
                {
                    double fillPrice = execution.Order.AverageFillPrice > 0.0
                        ? execution.Order.AverageFillPrice
                        : price;
                    entryTrade.MarkFilled(fillPrice);
                    tradeOutcomeText = "Posicion abierta";
                }
                else
                {
                    string fromEntrySignal = execution.Order.FromEntrySignal ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(fromEntrySignal)
                        && activeTrades.TryGetValue(fromEntrySignal, out ActiveTradeState exitedTrade))
                    {
                        activeTrades.Remove(fromEntrySignal);

                        if (orderName.IndexOf("Profit", StringComparison.OrdinalIgnoreCase) >= 0)
                            tradeOutcomeText = "TP alcanzado";
                        else if (orderName.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                            tradeOutcomeText = exitedTrade.TrailingActivated
                                ? "Trailing alcanzado"
                                : (exitedTrade.BreakEvenActivated ? "Breakeven alcanzado" : "SL alcanzado");
                        else if (marketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Flat)
                            tradeOutcomeText = "Posicion cerrada";
                    }
                    else if (marketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Flat)
                    {
                        tradeOutcomeText = "Posicion cerrada";
                    }
                }
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (activeTrades.Count > 0)
                    activeTrades.Clear();

                if (marketPosition == MarketPosition.Flat)
                    tradeOutcomeText = "Posicion cerrada";
            }

            RefreshStatusPanel();
        }

        private ScheduleSlot[] BuildScheduleSlots()
        {
            ScheduleSlot[] slots = new[]
            {
                CreateSlot(1, CheckHour1, AnchorHour1, nameof(CheckHour1), nameof(AnchorHour1)),
                CreateSlot(2, CheckHour2, AnchorHour2, nameof(CheckHour2), nameof(AnchorHour2)),
                CreateSlot(3, CheckHour3, AnchorHour3, nameof(CheckHour3), nameof(AnchorHour3)),
                CreateSlot(4, CheckHour4, AnchorHour4, nameof(CheckHour4), nameof(AnchorHour4)),
                CreateSlot(5, CheckHour5, AnchorHour5, nameof(CheckHour5), nameof(AnchorHour5))
            };

            ValidateActiveSlots(slots);
            Array.Sort(slots, CompareSlotsByCheckTimeThenSlotNumber);
            return slots;
        }

        private ScheduleSlot CreateSlot(int slotNumber, double checkHourDecimal, double anchorHourDecimal, string checkParameterName, string anchorParameterName)
        {
            bool checkDisabled = IsDisabledHour(checkHourDecimal);
            bool anchorDisabled = IsDisabledHour(anchorHourDecimal);

            if (checkDisabled != anchorDisabled)
            {
                throw new ArgumentException(
                    string.Format(
                        "Slot {0}: Check y Anchor deben configurarse juntos o desactivarse ambos con -1.",
                        slotNumber));
            }

            if (checkDisabled)
                return ScheduleSlot.CreateDisabled(slotNumber);

            TimeSpan checkTime = ConvertQuarterHourDecimalToTimeSpan(checkHourDecimal, checkParameterName);
            TimeSpan anchorTime = ConvertQuarterHourDecimalToTimeSpan(anchorHourDecimal, anchorParameterName);

            return new ScheduleSlot(slotNumber, checkHourDecimal, anchorHourDecimal, checkTime, anchorTime);
        }

        private void ValidateActiveSlots(ScheduleSlot[] slots)
        {
            int activeSlotCount = 0;
            HashSet<int> activeCheckTimes = new HashSet<int>();

            foreach (ScheduleSlot slot in slots)
            {
                if (!slot.IsEnabled)
                    continue;

                activeSlotCount++;

                if (!activeCheckTimes.Add(slot.CheckMinutes))
                {
                    throw new ArgumentException(
                        string.Format(
                            "Las horas Check activas no pueden repetirse. Duplicado detectado en Slot {0}.",
                            slot.SlotNumber));
                }
            }

            if (activeSlotCount == 0)
                throw new ArgumentException("Debes activar al menos un slot Anchor/Check.");
        }

        private static int CompareSlotsByCheckTimeThenSlotNumber(ScheduleSlot left, ScheduleSlot right)
        {
            int leftMinutes = left.IsEnabled ? left.CheckMinutes : int.MaxValue;
            int rightMinutes = right.IsEnabled ? right.CheckMinutes : int.MaxValue;

            int comparison = leftMinutes.CompareTo(rightMinutes);
            return comparison != 0 ? comparison : left.SlotNumber.CompareTo(right.SlotNumber);
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

        private bool TryExecuteSlot(ScheduleSlot slot, ref TradeDirection effectiveDirection)
        {
            double checkPrice = Close[0];
            panelSlotNumber = slot.SlotNumber;
            panelCheckPrice = checkPrice;
            tradeOutcomeText = "Sin evaluar";

            if (!TryResolveAnchorPoint(Time[0], slot, out double anchorPrice, out DateTime resolvedAnchorTime, out string anchorError))
            {
                panelAnchorPrice = double.NaN;
                panelDistanceTicks = double.NaN;
                panelMinDistanceMet = null;
                lastSignalText = string.Format("Slot {0}: anchor no resuelto", slot.SlotNumber);
                RefreshStatusPanel();

                Print(string.Format(
                    "{0} | Slot {1} | No se pudo resolver AnchorPrice: {2}.",
                    Time[0],
                    slot.SlotNumber,
                    anchorError));
                return false;
            }

            panelAnchorPrice = anchorPrice;

            double distanceTicks = Math.Abs(checkPrice - anchorPrice) / TickSize;
            panelDistanceTicks = distanceTicks;
            panelMinDistanceMet = distanceTicks >= MinDistanceTicks;
            if (distanceTicks < MinDistanceTicks)
            {
                lastSignalText = string.Format("Slot {0}: condición mínima NO cumplida", slot.SlotNumber);
                RefreshStatusPanel();

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
                lastSignalText = string.Format("Slot {0}: Anchor = Check, no trade", slot.SlotNumber);
                RefreshStatusPanel();

                Print(string.Format(
                    "{0} | Slot {1} | AnchorPrice igual a CheckPrice. No se abre operacion.",
                    Time[0],
                    slot.SlotNumber));
                return false;
            }

            if (!IsDirectionEnabled(signalDirection))
            {
                lastSignalText = string.Format(
                    "Slot {0}: senal {1} bloqueada",
                    slot.SlotNumber,
                    signalDirection == TradeDirection.Long ? "LONG" : "SHORT");
                RefreshStatusPanel();

                Print(string.Format(
                    "{0} | Slot {1} | Senal {2} ignorada porque ese lado esta deshabilitado.",
                    Time[0],
                    slot.SlotNumber,
                    signalDirection));
                return false;
            }

            if (effectiveDirection != TradeDirection.None && effectiveDirection != signalDirection)
            {
                lastSignalText = string.Format(
                    "Slot {0}: direccion {1} omitida por direccion activa",
                    slot.SlotNumber,
                    signalDirection == TradeDirection.Long ? "LONG" : "SHORT");
                RefreshStatusPanel();

                Print(string.Format(
                    "{0} | Slot {1} | Senal {2} omitida porque ya hay posicion o entrada pendiente en direccion {3}.",
                    Time[0],
                    slot.SlotNumber,
                    signalDirection,
                    effectiveDirection));
                return false;
            }

            if (!PassesDirectionalOpenGapFilter(signalDirection, out double sessionGapTicks))
            {
                lastSignalText = string.Format(
                    "Slot {0}: gap {1} no valido",
                    slot.SlotNumber,
                    signalDirection == TradeDirection.Long ? "alcista" : "bajista");
                RefreshStatusPanel();

                Print(string.Format(
                    "{0} | Slot {1} | Senal {2} omitida por gap de apertura. GapTicks={3}. MinGapTicks={4}.",
                    Time[0],
                    slot.SlotNumber,
                    signalDirection,
                    double.IsNaN(sessionGapTicks) ? "N/A" : sessionGapTicks.ToString("F2"),
                    MinOpenGapTicks));
                return false;
            }

            string signalName = BuildEntrySignalName(slot, signalDirection);
            activeTrades[signalName] = new ActiveTradeState(signalName, signalDirection);
            PrepareProtectiveOrders(signalName);

            if (signalDirection == TradeDirection.Long)
                EnterLong(UserQuantity, signalName);
            else
                EnterShort(UserQuantity, signalName);

            effectiveDirection = signalDirection;
            lastSignalText = string.Format(
                "Slot {0}: {1} lanzado",
                slot.SlotNumber,
                signalDirection == TradeDirection.Long ? "LONG" : "SHORT");
            RefreshStatusPanel();

            Print(string.Format(
                "{0} | Slot {1} | {2} | Anchor={3} Check={4} DistTicks={5:F2} GapTicks={6} Qty={7} (Anchor {8} {9}, bar {10:yyyy-MM-dd HH:mm:ss})",
                Time[0],
                slot.SlotNumber,
                signalDirection,
                anchorPrice,
                checkPrice,
                distanceTicks,
                double.IsNaN(sessionGapTicks) ? "N/A" : sessionGapTicks.ToString("F2"),
                UserQuantity,
                slot.AnchorUsesPreviousDataDay ? "PREV_DATA_DAY" : "SAME_DAY",
                FormatTime(slot.AnchorTime),
                resolvedAnchorTime));

            return true;
        }

        private void PrepareProtectiveOrders(string signalName)
        {
            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, TakeProfitTicks);
        }

        private void UpdateSessionGapState()
        {
            if (CurrentBar < 1)
            {
                currentSessionGapTicks = double.NaN;
                return;
            }

            if (!Bars.IsFirstBarOfSession)
                return;

            currentSessionGapTicks = (Open[0] - Close[1]) / TickSize;
        }

        private bool PassesDirectionalOpenGapFilter(TradeDirection signalDirection, out double sessionGapTicks)
        {
            sessionGapTicks = currentSessionGapTicks;

            if (!RequireDirectionalOpenGap)
                return true;

            if (double.IsNaN(sessionGapTicks))
                return false;

            if (signalDirection == TradeDirection.Long)
                return sessionGapTicks >= MinOpenGapTicks;

            if (signalDirection == TradeDirection.Short)
                return sessionGapTicks <= -MinOpenGapTicks;

            return false;
        }

        private void ManageActiveTrades()
        {
            if (activeTrades.Count == 0)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                activeTrades.Clear();
                return;
            }

            foreach (ActiveTradeState activeTrade in activeTrades.Values)
            {
                if (!activeTrade.IsFilled)
                    continue;

                if (activeTrade.Direction == TradeDirection.Long)
                    ManageLongTrade(activeTrade);
                else if (activeTrade.Direction == TradeDirection.Short)
                    ManageShortTrade(activeTrade);
            }
        }

        private void ManageLongTrade(ActiveTradeState activeTrade)
        {
            if (double.IsNaN(activeTrade.BestPrice) || High[0] > activeTrade.BestPrice)
                activeTrade.BestPrice = High[0];

            double favorableTicks = (activeTrade.BestPrice - activeTrade.EntryPrice) / TickSize;
            double desiredStopPrice = double.NaN;
            string stopReason = string.Empty;

            if (UseBreakEven && favorableTicks >= BreakEvenTriggerTicks)
            {
                desiredStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                    activeTrade.EntryPrice + (BreakEvenPlusTicks * TickSize));
                activeTrade.BreakEvenActivated = true;
                stopReason = "breakeven";
            }

            if (UseTrailingStop && favorableTicks >= TrailingTriggerTicks)
            {
                double extraTicks = favorableTicks - TrailingTriggerTicks;
                double stepCount = Math.Floor(extraTicks / TrailingStepTicks);
                double lockedTicks = TrailingDistanceTicks + (stepCount * TrailingStepTicks);
                double trailingStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                    activeTrade.EntryPrice + (lockedTicks * TickSize));

                if (double.IsNaN(desiredStopPrice) || trailingStopPrice > desiredStopPrice)
                    desiredStopPrice = trailingStopPrice;

                activeTrade.TrailingActivated = true;
                stopReason = "trailing";
            }

            if (double.IsNaN(desiredStopPrice))
                return;

            if (double.IsNaN(activeTrade.CurrentStopPrice) || desiredStopPrice > activeTrade.CurrentStopPrice)
            {
                activeTrade.CurrentStopPrice = desiredStopPrice;
                SetStopLoss(activeTrade.SignalName, CalculationMode.Price, desiredStopPrice, false);
                lastSignalText = string.Format("{0}: {1} LONG activo", activeTrade.SignalName, stopReason);
            }
        }

        private void ManageShortTrade(ActiveTradeState activeTrade)
        {
            if (double.IsNaN(activeTrade.BestPrice) || Low[0] < activeTrade.BestPrice)
                activeTrade.BestPrice = Low[0];

            double favorableTicks = (activeTrade.EntryPrice - activeTrade.BestPrice) / TickSize;
            double desiredStopPrice = double.NaN;
            string stopReason = string.Empty;

            if (UseBreakEven && favorableTicks >= BreakEvenTriggerTicks)
            {
                desiredStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                    activeTrade.EntryPrice - (BreakEvenPlusTicks * TickSize));
                activeTrade.BreakEvenActivated = true;
                stopReason = "breakeven";
            }

            if (UseTrailingStop && favorableTicks >= TrailingTriggerTicks)
            {
                double extraTicks = favorableTicks - TrailingTriggerTicks;
                double stepCount = Math.Floor(extraTicks / TrailingStepTicks);
                double lockedTicks = TrailingDistanceTicks + (stepCount * TrailingStepTicks);
                double trailingStopPrice = Instrument.MasterInstrument.RoundToTickSize(
                    activeTrade.EntryPrice - (lockedTicks * TickSize));

                if (double.IsNaN(desiredStopPrice) || trailingStopPrice < desiredStopPrice)
                    desiredStopPrice = trailingStopPrice;

                activeTrade.TrailingActivated = true;
                stopReason = "trailing";
            }

            if (double.IsNaN(desiredStopPrice))
                return;

            if (double.IsNaN(activeTrade.CurrentStopPrice) || desiredStopPrice < activeTrade.CurrentStopPrice)
            {
                activeTrade.CurrentStopPrice = desiredStopPrice;
                SetStopLoss(activeTrade.SignalName, CalculationMode.Price, desiredStopPrice, false);
                lastSignalText = string.Format("{0}: {1} SHORT activo", activeTrade.SignalName, stopReason);
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

        private string BuildEntrySignalName(ScheduleSlot slot, TradeDirection direction)
        {
            string directionName = direction == TradeDirection.Long ? "Long" : "Short";
            return string.Format("Anchor{0}_S{1}_{2}", directionName, slot.SlotNumber, ToDay(Time[0]));
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

            ResetPanelTradeState();
        }

        private void UpdateCurrentAnchorVisual(DateTime referenceTime)
        {
            if (ChartControl == null || scheduleSlots == null || scheduleSlots.Length == 0)
                return;

            List<ResolvedAnchorVisual> resolvedAnchors = new List<ResolvedAnchorVisual>();

            for (int slotIndex = 0; slotIndex < scheduleSlots.Length; slotIndex++)
            {
                ScheduleSlot slot = scheduleSlots[slotIndex];
                if (!slot.IsEnabled)
                    continue;

                if (!TryResolveAnchorPoint(referenceTime, slot, out double resolvedAnchorPrice, out DateTime resolvedAnchorTime, out string error))
                    continue;

                resolvedAnchors.Add(new ResolvedAnchorVisual(slot.SlotNumber, resolvedAnchorTime, resolvedAnchorPrice));
            }

            resolvedAnchors.Sort(CompareResolvedAnchors);
            DrawResolvedAnchorSegments(referenceTime, resolvedAnchors);
        }

        private int GetBarsAgoFromTime(DateTime targetTime)
        {
            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                if (Time[barsAgo] <= targetTime)
                    return barsAgo;
            }

            return CurrentBar;
        }

        private static int CompareResolvedAnchors(ResolvedAnchorVisual left, ResolvedAnchorVisual right)
        {
            int comparison = left.AnchorTime.CompareTo(right.AnchorTime);
            return comparison != 0 ? comparison : left.SlotNumber.CompareTo(right.SlotNumber);
        }

        private void DrawResolvedAnchorSegments(DateTime referenceTime, List<ResolvedAnchorVisual> resolvedAnchors)
        {
            int dayKey = ToDay(referenceTime);

            if (resolvedAnchors.Count == 0)
            {
                RemoveUnusedAnchorSegments(dayKey, 0);
                currentAnchorSegmentStartTime = DateTime.MinValue;
                currentAnchorSegmentPrice = double.NaN;
                currentAnchorSegmentTag = string.Empty;
                return;
            }

            for (int segmentIndex = 0; segmentIndex < resolvedAnchors.Count; segmentIndex++)
            {
                ResolvedAnchorVisual currentAnchor = resolvedAnchors[segmentIndex];
                int startBarsAgo = GetBarsAgoFromTime(currentAnchor.AnchorTime);
                int endBarsAgo = segmentIndex + 1 < resolvedAnchors.Count
                    ? GetBarsAgoFromTime(resolvedAnchors[segmentIndex + 1].AnchorTime)
                    : 0;

                Draw.Line(this, GetAnchorSegmentTag(dayKey, segmentIndex), false,
                    startBarsAgo, currentAnchor.AnchorPrice,
                    endBarsAgo, currentAnchor.AnchorPrice,
                    Brushes.DeepSkyBlue, DashStyleHelper.Solid, 2);
            }

            RemoveUnusedAnchorSegments(dayKey, resolvedAnchors.Count);

            ResolvedAnchorVisual latestAnchor = resolvedAnchors[resolvedAnchors.Count - 1];
            currentAnchorSegmentStartTime = latestAnchor.AnchorTime;
            currentAnchorSegmentPrice = latestAnchor.AnchorPrice;
            currentAnchorSegmentTag = GetAnchorSegmentTag(dayKey, resolvedAnchors.Count - 1);
        }

        private void RemoveUnusedAnchorSegments(int dayKey, int segmentCount)
        {
            if (lastAnchorVisualDayKey == dayKey && lastAnchorVisualSegmentCount > segmentCount)
            {
                for (int segmentIndex = segmentCount; segmentIndex < lastAnchorVisualSegmentCount; segmentIndex++)
                    RemoveDrawObject(GetAnchorSegmentTag(dayKey, segmentIndex));
            }

            lastAnchorVisualDayKey = dayKey;
            lastAnchorVisualSegmentCount = segmentCount;
        }

        private string GetAnchorSegmentTag(int dayKey, int segmentIndex)
        {
            return string.Format("AnchorSegment_{0}_{1}", dayKey, segmentIndex);
        }

        private void RefreshActiveAnchorSegment()
        {
            if (ChartControl == null)
                return;

            if (currentAnchorSegmentStartTime == DateTime.MinValue || double.IsNaN(currentAnchorSegmentPrice) || string.IsNullOrEmpty(currentAnchorSegmentTag))
                return;

            int startBarsAgo = GetBarsAgoFromTime(currentAnchorSegmentStartTime);

            Draw.Line(this, currentAnchorSegmentTag, false,
                startBarsAgo, currentAnchorSegmentPrice,
                0, currentAnchorSegmentPrice,
                Brushes.DeepSkyBlue, DashStyleHelper.Solid, 2);
        }

        private void ResetPanelTradeState()
        {
            panelAnchorPrice = double.NaN;
            panelCheckPrice = double.NaN;
            panelDistanceTicks = double.NaN;
            panelMinDistanceMet = null;
            tradeOutcomeText = "Sin evaluar";
            lastSignalText = "Sin señal";
            panelSlotNumber = 0;
        }

        private void UpdateCurrentAnchorPanelState(DateTime referenceTime)
        {
            if (scheduleSlots == null || scheduleSlots.Length == 0)
                return;

            ScheduleSlot latestSlot = null;
            DateTime latestAnchorTime = DateTime.MinValue;
            double latestAnchorPrice = double.NaN;

            for (int slotIndex = 0; slotIndex < scheduleSlots.Length; slotIndex++)
            {
                ScheduleSlot slot = scheduleSlots[slotIndex];
                if (!slot.IsEnabled)
                    continue;

                if (!TryResolveAnchorPoint(referenceTime, slot, out double resolvedAnchorPrice, out DateTime resolvedAnchorTime, out string error))
                    continue;

                if (latestSlot == null || resolvedAnchorTime > latestAnchorTime)
                {
                    latestSlot = slot;
                    latestAnchorTime = resolvedAnchorTime;
                    latestAnchorPrice = resolvedAnchorPrice;
                }
            }

            if (latestSlot == null)
            {
                panelAnchorPrice = double.NaN;
                panelSlotNumber = 0;
                return;
            }

            bool slotChanged = panelSlotNumber != latestSlot.SlotNumber;
            panelAnchorPrice = latestAnchorPrice;

            if (slotChanged && !latestSlot.ProcessedToday)
            {
                panelCheckPrice = double.NaN;
                panelDistanceTicks = double.NaN;
                panelMinDistanceMet = null;
                tradeOutcomeText = "Sin evaluar";
                lastSignalText = "Esperando check";
            }

            panelSlotNumber = latestSlot.SlotNumber;
        }

        private void TryCreatePanel()
        {
            if (ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (statusPanelBorder != null)
                        return;

                    chartWindow = Window.GetWindow(ChartControl.Parent) as Chart;
                    if (chartWindow == null)
                        return;

                    chartTrader = chartWindow.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                    if (chartTrader == null)
                        return;

                    chartTraderGrid = chartTrader.FindName("grdMain") as Grid;
                    if (chartTraderGrid == null)
                        return;

                    statusPanelStack = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Margin = new Thickness(0)
                    };

                    tbBotName = MakeTextBlock("", Brushes.DeepSkyBlue, FontWeights.Bold, PanelFontSize + 1);
                    tbLicense = MakeTextBlock("", Brushes.LimeGreen, FontWeights.Normal, PanelFontSize);
                    tbExpiry = MakeTextBlock("", Brushes.Gold, FontWeights.Normal, PanelFontSize);

                    sep1 = MakeSeparator();

                    tbAnchor = MakeTextBlock("", PanelTextColor, FontWeights.Normal, PanelFontSize);
                    tbCheck = MakeTextBlock("", PanelTextColor, FontWeights.Normal, PanelFontSize);
                    tbDistance = MakeTextBlock("", PanelTextColor, FontWeights.Normal, PanelFontSize);
                    tbMinTicks = MakeTextBlock("", Brushes.Gold, FontWeights.Bold, PanelFontSize);

                    sep2 = MakeSeparator();

                    tbSignal = MakeTextBlock("", Brushes.DeepSkyBlue, FontWeights.Normal, PanelFontSize);
                    tbResult = MakeTextBlock("", Brushes.Gainsboro, FontWeights.Bold, PanelFontSize);

                    statusPanelStack.Children.Add(tbBotName);
                    statusPanelStack.Children.Add(tbLicense);
                    statusPanelStack.Children.Add(tbExpiry);
                    statusPanelStack.Children.Add(sep1);
                    statusPanelStack.Children.Add(tbAnchor);
                    statusPanelStack.Children.Add(tbCheck);
                    statusPanelStack.Children.Add(tbDistance);
                    statusPanelStack.Children.Add(tbMinTicks);
                    statusPanelStack.Children.Add(sep2);
                    statusPanelStack.Children.Add(tbSignal);
                    statusPanelStack.Children.Add(tbResult);

                    statusPanelBorder = new Border
                    {
                        Name = "DotelAnchorStatusPanel",
                        Background = PanelBackgroundColor,
                        BorderBrush = Brushes.DimGray,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8),
                        Margin = new Thickness(6, 6, 6, 6),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        MinWidth = 0,
                        Child = statusPanelStack
                    };

                    int targetRow = chartTraderGrid.RowDefinitions.Count;
                    chartTraderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    Grid.SetRow(statusPanelBorder, targetRow);
                    Grid.SetColumn(statusPanelBorder, 0);

                    System.Windows.Controls.Panel.SetZIndex(statusPanelBorder, 99999);
                    chartTraderGrid.Children.Add(statusPanelBorder);

                    RefreshStatusPanel();
                }
                catch (Exception ex)
                {
                    Print("Error creando panel visual en Chart Trader: " + ex.Message);
                }
            });
        }

        private void RemoveStatusPanel()
        {
            if (ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (chartTraderGrid != null && statusPanelBorder != null && chartTraderGrid.Children.Contains(statusPanelBorder))
                        chartTraderGrid.Children.Remove(statusPanelBorder);
                }
                catch (Exception ex)
                {
                    Print("Error removiendo panel visual: " + ex.Message);
                }
                finally
                {
                    statusPanelBorder = null;
                    statusPanelStack = null;
                    chartWindow = null;
                    chartTrader = null;
                    chartTraderGrid = null;

                    tbBotName = null;
                    tbLicense = null;
                    tbExpiry = null;
                    tbAnchor = null;
                    tbCheck = null;
                    tbDistance = null;
                    tbMinTicks = null;
                    tbSignal = null;
                    tbResult = null;
                    sep1 = null;
                    sep2 = null;
                }
            });
        }

        private void RefreshStatusPanel()
        {
            if (ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (statusPanelBorder == null)
                        return;

                    string slotText = panelSlotNumber > 0 ? string.Format(" [S{0}]", panelSlotNumber) : string.Empty;
                    string anchorText = double.IsNaN(panelAnchorPrice) ? "--" : FormatPrice(panelAnchorPrice);
                    string checkText = double.IsNaN(panelCheckPrice) ? "--" : FormatPrice(panelCheckPrice);
                    string distanceText = double.IsNaN(panelDistanceTicks) ? "--" : panelDistanceTicks.ToString("F2");

                    string minDistanceStatusText = !panelMinDistanceMet.HasValue
                        ? "Pendiente"
                        : (panelMinDistanceMet.Value ? "Sí cumple" : "No cumple");

                    Brush licenseBrush = licenseStatusText == "EXPIRADA" || licenseStatusText == "INVALIDA"
                        ? Brushes.Red
                        : Brushes.LimeGreen;

                    tbBotName.Text = Name;
                    tbBotName.FontFamily = new FontFamily(PanelFontFamily);
                    tbBotName.FontSize = PanelFontSize + 1;
                    tbBotName.Foreground = Brushes.DeepSkyBlue;

                    tbLicense.Text = string.Format("Licencia: {0}", licenseStatusText);
                    tbLicense.FontFamily = new FontFamily(PanelFontFamily);
                    tbLicense.FontSize = PanelFontSize;
                    tbLicense.Foreground = licenseBrush;

                    tbExpiry.Text = string.Format("Expira: {0}", GetPanelExpiryText());
                    tbExpiry.FontFamily = new FontFamily(PanelFontFamily);
                    tbExpiry.FontSize = PanelFontSize;
                    tbExpiry.Foreground = Brushes.Gold;

                    tbAnchor.Text = string.Format("Anchor Price{0}: {1}", slotText, anchorText);
                    tbAnchor.FontFamily = new FontFamily(PanelFontFamily);
                    tbAnchor.FontSize = PanelFontSize;
                    tbAnchor.Foreground = PanelTextColor;

                    tbCheck.Text = string.Format("Check Price{0}: {1}", slotText, checkText);
                    tbCheck.FontFamily = new FontFamily(PanelFontFamily);
                    tbCheck.FontSize = PanelFontSize;
                    tbCheck.Foreground = PanelTextColor;

                    tbDistance.Text = string.Format("Distancia: {0} ticks", distanceText);
                    tbDistance.FontFamily = new FontFamily(PanelFontFamily);
                    tbDistance.FontSize = PanelFontSize;
                    tbDistance.Foreground = PanelTextColor;

                    tbMinTicks.Text = string.Format("Min Dist ({0}): {1}", MinDistanceTicks, minDistanceStatusText);
                    tbMinTicks.FontFamily = new FontFamily(PanelFontFamily);
                    tbMinTicks.FontSize = PanelFontSize;
                    tbMinTicks.Foreground = !panelMinDistanceMet.HasValue
                        ? Brushes.Gold
                        : (panelMinDistanceMet.Value ? Brushes.LimeGreen : Brushes.Red);

                    tbSignal.Text = string.Format("Señal: {0}", lastSignalText);
                    tbSignal.FontFamily = new FontFamily(PanelFontFamily);
                    tbSignal.FontSize = PanelFontSize;
                    tbSignal.Foreground = Brushes.DeepSkyBlue;

                    tbResult.Text = string.Format("Resultado: {0}", tradeOutcomeText);
                    tbResult.FontFamily = new FontFamily(PanelFontFamily);
                    tbResult.FontSize = PanelFontSize;
                    tbResult.Foreground =
                        tradeOutcomeText.Contains("TP") ? Brushes.LimeGreen :
                        tradeOutcomeText.Contains("SL") ? Brushes.Red :
                        Brushes.Gainsboro;

                    statusPanelBorder.Background = PanelBackgroundColor;
                }
                catch (Exception ex)
                {
                    Print("Error refrescando panel visual: " + ex.Message);
                }
            });
        }

        private TextBlock MakeTextBlock(string text, Brush foreground, FontWeight weight, double fontSize)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontWeight = weight,
                FontSize = fontSize,
                FontFamily = new FontFamily(PanelFontFamily),
                Margin = new Thickness(0, 1, 0, 1),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private Border MakeSeparator()
        {
            return new Border
            {
                Height = 1,
                Margin = new Thickness(0, 6, 0, 6),
                Background = Brushes.DimGray
            };
        }

        private string FormatPrice(double price)
        {
            return Instrument.MasterInstrument.FormatPrice(price);
        }

        private string GetPanelExpiryText()
        {
            return ExpirationDate.ToString("dd/MM/yyyy");
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

        private TimeSpan ConvertQuarterHourDecimalToTimeSpan(double hourDecimal, string parameterName)
        {
            if (!TryParseQuarterHourDecimal(hourDecimal, out TimeSpan time, out string error))
                throw new ArgumentException(parameterName + " invalido: " + error, parameterName);

            return time;
        }

        private bool TryParseQuarterHourDecimal(double hourDecimal, out TimeSpan time, out string error)
        {
            time = TimeSpan.Zero;
            error = null;

            if (hourDecimal < 0 || hourDecimal > 23.75)
            {
                error = string.Format("Debe estar entre 0 y 23.75. Recibido: {0}.", hourDecimal);
                return false;
            }

            double quarters = hourDecimal / 0.25;
            double roundedQuarters = Math.Round(quarters);
            if (Math.Abs(quarters - roundedQuarters) > 1e-9)
            {
                error = string.Format("Debe ser multiplo de 0.25. Recibido: {0}.", hourDecimal);
                return false;
            }

            int totalMinutes = (int)Math.Round(hourDecimal * 60.0);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            if (hours < 0 || hours > 23 || (minutes != 0 && minutes != 15 && minutes != 30 && minutes != 45))
            {
                error = string.Format(
                    "Formato invalido tras conversion. Recibido: {0} => {1:00}:{2:00}.",
                    hourDecimal,
                    hours,
                    minutes);
                return false;
            }

            time = new TimeSpan(hours, minutes, 0);
            return true;
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

        private string NormalizeMachineId(string machineId)
        {
            return string.IsNullOrWhiteSpace(machineId)
                ? string.Empty
                : machineId.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        }

        private string GetCurrentMachineId()
        {
            try
            {
                string machineId = string.Empty;

                try
                {
                    machineId = NinjaTrader.Cbi.License.MachineId;
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(machineId))
                    return NormalizeMachineId(machineId);

                Type globalsType = typeof(NinjaTrader.Core.Globals);

                var prop = globalsType.GetProperty("MachineId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (prop != null)
                {
                    object value = prop.GetValue(null, null);
                    machineId = value == null ? string.Empty : value.ToString();
                    if (!string.IsNullOrWhiteSpace(machineId))
                        return NormalizeMachineId(machineId);
                }

                var field = globalsType.GetField("MachineId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (field != null)
                {
                    object value = field.GetValue(null);
                    machineId = value == null ? string.Empty : value.ToString();
                    if (!string.IsNullOrWhiteSpace(machineId))
                        return NormalizeMachineId(machineId);
                }

                Print("No se pudo obtener el Machine ID desde NinjaTrader.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Print("No se pudo leer el Machine ID: " + ex.Message);
                return string.Empty;
            }
        }

        private bool IsMachineIdValid()
        {
            string currentMachineId = GetCurrentMachineId();
            if (string.IsNullOrWhiteSpace(currentMachineId))
                return false;

            for (int i = 0; i < AllowedMachineIds.Length; i++)
            {
                string allowedMachineId = NormalizeMachineId(AllowedMachineIds[i]);
                if (!string.IsNullOrWhiteSpace(allowedMachineId)
                    && string.Equals(currentMachineId, allowedMachineId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private enum TradeDirection
        {
            None,
            Long,
            Short
        }

        private sealed class ActiveTradeState
        {
            public ActiveTradeState(string signalName, TradeDirection direction)
            {
                SignalName = signalName;
                Direction = direction;
                EntryPrice = double.NaN;
                BestPrice = double.NaN;
                CurrentStopPrice = double.NaN;
            }

            public string SignalName { get; private set; }
            public TradeDirection Direction { get; private set; }
            public bool IsFilled { get; private set; }
            public double EntryPrice { get; private set; }
            public double BestPrice { get; set; }
            public double CurrentStopPrice { get; set; }
            public bool BreakEvenActivated { get; set; }
            public bool TrailingActivated { get; set; }

            public void MarkFilled(double entryPrice)
            {
                IsFilled = true;
                EntryPrice = entryPrice;

                if (double.IsNaN(BestPrice))
                    BestPrice = entryPrice;
            }
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

        private sealed class ResolvedAnchorVisual
        {
            public ResolvedAnchorVisual(int slotNumber, DateTime anchorTime, double anchorPrice)
            {
                SlotNumber = slotNumber;
                AnchorTime = anchorTime;
                AnchorPrice = anchorPrice;
            }

            public int SlotNumber { get; private set; }
            public DateTime AnchorTime { get; private set; }
            public double AnchorPrice { get; private set; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Panel Font Size", GroupName = "05. Panel", Order = 0)]
        [Range(8, 40)]
        public int PanelFontSize
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Panel Font Family", GroupName = "05. Panel", Order = 1)]
        public string PanelFontFamily
        { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Panel Text Color", GroupName = "05. Panel", Order = 2)]
        public Brush PanelTextColor
        { get; set; }

        [Browsable(false)]
        public string PanelTextColorSerializable
        {
            get { return Serialize.BrushToString(PanelTextColor); }
            set { PanelTextColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Panel Background", GroupName = "05. Panel", Order = 3)]
        public Brush PanelBackgroundColor
        { get; set; }

        [Browsable(false)]
        public string PanelBackgroundColorSerializable
        {
            get { return Serialize.BrushToString(PanelBackgroundColor); }
            set { PanelBackgroundColor = Serialize.StringToBrush(value); }
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
        [Display(Name = "Requerir gap apertura", GroupName = "03. Reglas", Order = 1)]
        public bool RequireDirectionalOpenGap
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Gap minimo apertura (ticks)", GroupName = "03. Reglas", Order = 2)]
        public int MinOpenGapTicks
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
        [Display(Name = "Usar breakeven", GroupName = "04. Riesgo", Order = 2)]
        public bool UseBreakEven
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BE trigger (ticks)", GroupName = "04. Riesgo", Order = 3)]
        public int BreakEvenTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE plus (ticks)", GroupName = "04. Riesgo", Order = 4)]
        public int BreakEvenPlusTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar trailing stop", GroupName = "04. Riesgo", Order = 5)]
        public bool UseTrailingStop
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trailing trigger (ticks)", GroupName = "04. Riesgo", Order = 6)]
        public int TrailingTriggerTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trailing distance (ticks)", GroupName = "04. Riesgo", Order = 7)]
        public int TrailingDistanceTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trailing step (ticks)", GroupName = "04. Riesgo", Order = 8)]
        public int TrailingStepTicks
        { get; set; }
    }
}
