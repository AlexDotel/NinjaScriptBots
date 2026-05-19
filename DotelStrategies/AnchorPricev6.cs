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
    public class AnchorPricev6 : Strategy
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
        private double panelAnchorPrice = double.NaN;
        private double panelCheckPrice = double.NaN;
        private double panelDistanceTicks = double.NaN;
        private bool? panelMinDistanceMet;
        private string tradeOutcomeText = "Sin evaluar";
        private string lastSignalText = "Sin señal";
        private string licenseStatusText = "ACTIVA";
        private int panelSlotNumber;

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
                Description = "Anchor multi-slot basado en AnchorPriceAdvancedBot. Dibuja el anchor tan pronto como existe.";
                Name = "AnchorPricev6";
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
                StopLossTicks = 20;
                TakeProfitTicks = 20;

                PanelFontSize = 14;
                PanelFontFamily = "Arial";
                PanelTextColor = Brushes.White;
                PanelBackgroundColor = Brushes.Black;
                licenseStatusText = "INTERNA";
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
                if (orderName.IndexOf("Profit", StringComparison.OrdinalIgnoreCase) >= 0)
                    tradeOutcomeText = "TP alcanzado";
                else if (orderName.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                    tradeOutcomeText = "SL alcanzado";
                else if (marketPosition == MarketPosition.Flat)
                    tradeOutcomeText = "Posición cerrada";
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
                    "Slot {0}: señal {1} bloqueada",
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
                    "Slot {0}: {1} omitida por dirección activa",
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

            string signalName = BuildEntrySignalName(slot, signalDirection);
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
                "{0} | Slot {1} | {2} | Anchor={3} Check={4} DistTicks={5:F2} Qty={6} (Anchor {7} {8}, bar {9:yyyy-MM-dd HH:mm:ss})",
                Time[0],
                slot.SlotNumber,
                signalDirection,
                anchorPrice,
                checkPrice,
                distanceTicks,
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
            return "--";
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
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "04. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "04. Riesgo", Order = 1)]
        public int TakeProfitTicks
        { get; set; }
    }
}
