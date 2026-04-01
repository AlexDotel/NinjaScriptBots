#region Using declarations
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using System;
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
    public class AnchorSixBetaTrailing : Strategy
    {
        // ====== Inputs (Usuario) ======

        [NinjaScriptProperty]
        [Display(Name = "Cantidad (lotes/contratos)", GroupName = "01. Orden", Order = 0)]
        [Range(1, int.MaxValue)]
        public int UserQuantity { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Permitir COMPRAS (Long)", GroupName = "01. Orden", Order = 1)]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Permitir VENTAS (Short)", GroupName = "01. Orden", Order = 2)]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Hora CHECK (decimal, múltiplo 0.25)", GroupName = "02. Horas", Order = 0)]
        public double CheckHourDecimal { get; set; } = 16.50;

        [NinjaScriptProperty]
        [Display(Name = "Hora ANCHOR (decimal, múltiplo 0.25)", GroupName = "02. Horas", Order = 1)]
        public double AnchorHourDecimal { get; set; } = 13.00;

        [NinjaScriptProperty]
        [Display(Name = "Distancia mínima (ticks) Anchor vs Actual", GroupName = "03. Reglas", Order = 0)]
        [Range(0, int.MaxValue)]
        public int MinDistanceTicks { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "StopLoss (ticks)", GroupName = "04. Salidas", Order = 0)]
        [Range(1, int.MaxValue)]
        public int StopLossTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "TakeProfit (ticks)", GroupName = "04. Salidas", Order = 1)]
        [Range(1, int.MaxValue)]
        public int TakeProfitTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Usar Trailing Stop", GroupName = "04. Salidas", Order = 2)]
        public bool UseTrailingStop { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Trailing Trigger (ticks)", GroupName = "04. Salidas", Order = 3)]
        [Range(1, int.MaxValue)]
        public int TrailingTriggerTicks { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Trailing Distance (ticks)", GroupName = "04. Salidas", Order = 4)]
        [Range(1, int.MaxValue)]
        public int TrailingDistanceTicks { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name = "Trailing Step (ticks)", GroupName = "04. Salidas", Order = 5)]
        [Range(1, int.MaxValue)]
        public int TrailingStepTicks { get; set; } = 2;

        // ====== Panel visual ======

        [NinjaScriptProperty]
        [Display(Name = "Panel Font Size", GroupName = "05. Panel", Order = 0)]
        [Range(8, 40)]
        public int PanelFontSize { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "Panel Font Family", GroupName = "05. Panel", Order = 1)]
        public string PanelFontFamily { get; set; } = "Arial";

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Panel Text Color", GroupName = "05. Panel", Order = 2)]
        public Brush PanelTextColor { get; set; } = Brushes.White;

        [Browsable(false)]
        public string PanelTextColorSerializable
        {
            get { return Serialize.BrushToString(PanelTextColor); }
            set { PanelTextColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Panel Background", GroupName = "05. Panel", Order = 3)]
        public Brush PanelBackgroundColor { get; set; } = Brushes.Black;

        [Browsable(false)]
        public string PanelBackgroundColorSerializable
        {
            get { return Serialize.BrushToString(PanelBackgroundColor); }
            set { PanelBackgroundColor = Serialize.StringToBrush(value); }
        }

        // ====== Estado interno ======
        private TimeSpan checkTime;
        private TimeSpan anchorTime;

        private int currentYyyyMmDd = -1;
        private bool tradedToday = false;
        private bool checkExecuted = false;

        // Si AnchorHour > CheckHour => el anchor corresponde al último día con datos antes del día actual
        private bool anchorUsesPreviousDataDay = false;

        private double anchorPrice = double.NaN;

        // ====== Trailing stop ======
        private double highestPriceSinceEntry = double.NaN;
        private double lowestPriceSinceEntry = double.NaN;
        private double currentTrailingStopPrice = double.NaN;
        private bool trailingArmed = false;

        // ====== Segmentos visuales del anchor en el gráfico ======
        private DateTime currentAnchorSegmentStartTime = DateTime.MinValue;
        private double currentAnchorSegmentPrice = double.NaN;
        private string currentAnchorSegmentTag = string.Empty;
        private int anchorSegmentCounter = 0;

        // =======================================================================================================================================================
        // Vencimiento: funciona hasta el 01/04/2026 (inclusive)
        private static readonly DateTime ExpirationDate = new DateTime(2026, 4, 1);
        private const string AllowedMachineId = "64B1CDD3679276C4900736F898D11557"; //64B1CDD3679276C4900736F898D11557 Mi machine id.
        private bool expirationPrinted = false;
        private bool invalidLicensePrinted = false;
        // =======================================================================================================================================================

        // ====== Estado visual ======
        private double checkPrice = double.NaN;
        private double lastDistanceTicks = double.NaN;
        private bool? minDistanceMet = null;
        private string tradeOutcomeText = "Not evaluated";
        private string lastSignalText = "No signal";
        private string licenseStatusText = "ACTIVE";

        // ====== Panel WPF ======
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
                Name                                      = "AnchorSixBetaTrailing";
                Description                               = "Machine ID: " + AllowedMachineId;
                Calculate                                 = Calculate.OnBarClose;

                EntriesPerDirection                       = 1;
                EntryHandling                             = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy              = true;
                ExitOnSessionCloseSeconds                 = 30;

                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior                             = StartBehavior.WaitUntilFlat;
            }
            else if (State == State.Configure)
            {
                if (!TryParseQuarterHourDecimal(CheckHourDecimal, out checkTime, out string err1))
                    throw new Exception("CheckHourDecimal inválido: " + err1);

                if (!TryParseQuarterHourDecimal(AnchorHourDecimal, out anchorTime, out string err2))
                    throw new Exception("AnchorHourDecimal inválido: " + err2);

                if (StopLossTicks <= 0 || TakeProfitTicks <= 0)
                    throw new Exception("StopLossTicks y TakeProfitTicks deben ser > 0.");

                if (UserQuantity <= 0)
                    throw new Exception("UserQuantity debe ser >= 1.");

                if (UseTrailingStop)
                {
                    if (TrailingTriggerTicks <= 0)
                        throw new Exception("TrailingTriggerTicks debe ser > 0.");

                    if (TrailingDistanceTicks <= 0)
                        throw new Exception("TrailingDistanceTicks debe ser > 0.");

                    if (TrailingStepTicks <= 0)
                        throw new Exception("TrailingStepTicks debe ser > 0.");
                }

                anchorUsesPreviousDataDay = anchorTime > checkTime;

                SetStopLoss("AnchorLong", CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget("AnchorLong", CalculationMode.Ticks, TakeProfitTicks);

                SetStopLoss("AnchorShort", CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget("AnchorShort", CalculationMode.Ticks, TakeProfitTicks);
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
            if (CurrentBar < 2)
                return;

            TryCreatePanel();

            // ====== Licencia por Machine ID ======
            if (!IsMachineIdValid())
            {
                licenseStatusText = "INVALID";
                lastSignalText    = "Invalid License";
                tradeOutcomeText  = "Bot blocked";
                RefreshStatusPanel();

                if (!invalidLicensePrinted)
                {
                    invalidLicensePrinted = true;
                    Print($"{Time[0]} | Invalid license. Current Machine ID: {GetCurrentMachineId()} | Authorized Machine ID: {AllowedMachineId}");
                }
                return;
            }

            // ====== Expiración ======
            if (Time[0].Date > ExpirationDate)
            {
                licenseStatusText = "EXPIRED";
                RefreshStatusPanel();

                if (!expirationPrinted)
                {
                    expirationPrinted = true;
                    Print($"{Time[0]} | Strategy expired. Valid until {ExpirationDate:dd/MM/yyyy} (inclusive). Contact the developer: @isdotel on Discord");
                }
                return;
            }

            licenseStatusText = "ACTIVE";
            invalidLicensePrinted = false;

            ManageTrailingStop();

            // Reset diario
            int barDate = ToDay(Time[0]);
            if (barDate != currentYyyyMmDd)
            {
                currentYyyyMmDd   = barDate;
                tradedToday       = false;
                checkExecuted     = false;
                anchorPrice       = double.NaN;

                checkPrice        = double.NaN;
                lastDistanceTicks = double.NaN;
                minDistanceMet    = null;
                tradeOutcomeText  = "Not evaluated";
                lastSignalText    = "No signal";
            }

            RefreshStatusPanel();
            RefreshActiveAnchorSegment();

            if (tradedToday || checkExecuted)
                return;

            TimeSpan now = Time[0].TimeOfDay;

            if (!checkExecuted && now >= checkTime)
            {
                checkExecuted = true;
                checkPrice    = Close[0];

                if (!TryResolveAnchorPoint(Time[0], out anchorPrice, out DateTime resolvedAnchorTime, out string anchorErr))
                {
                    lastSignalText = "Could not resolve anchor";
                    Print($"{Time[0]} | Could not resolve AnchorPrice: {anchorErr}. Trade skipped for the day.");
                    RefreshStatusPanel();
                    return;
                }

                UpdateAnchorSegment(resolvedAnchorTime, anchorPrice);

                double currentPrice = Close[0];

                double distanceTicks = Math.Abs(currentPrice - anchorPrice) / TickSize;
                lastDistanceTicks = distanceTicks;
                minDistanceMet    = distanceTicks >= MinDistanceTicks;

                if (distanceTicks < MinDistanceTicks)
                {
                    lastSignalText = "Minimum condition NOT met";
                    Print($"{Time[0]} | Insufficient distance: {distanceTicks:F2} ticks < MinDistanceTicks({MinDistanceTicks}). No trade.");
                    RefreshStatusPanel();
                    return;
                }

                int qty = UserQuantity;

                if (anchorPrice < currentPrice)
                {
                    if (!EnableShorts)
                    {
                        lastSignalText = "SHORT signal blocked";
                        Print($"{Time[0]} | SHORT signal but EnableShorts=false. No trade.");
                        RefreshStatusPanel();
                        return;
                    }

                    ResetTrailingState();
                    lastSignalText = "SHORT placed";
                    EnterShort(qty, "AnchorShort");
                    tradedToday = true;

                    Print($"{Time[0]} | SHORT | Anchor={anchorPrice} Current={currentPrice} Dist={distanceTicks:F2} ticks Qty={qty} (Anchor {(anchorUsesPreviousDataDay ? "PREV_DATA_DAY" : "HOY")} {anchorTime})");
                }
                else if (anchorPrice > currentPrice)
                {
                    if (!EnableLongs)
                    {
                        lastSignalText = "LONG signal blocked";
                        Print($"{Time[0]} | LONG signal but EnableLongs=false. No trade.");
                        RefreshStatusPanel();
                        return;
                    }

                    ResetTrailingState();
                    lastSignalText = "LONG placed";
                    EnterLong(qty, "AnchorLong");
                    tradedToday = true;

                    Print($"{Time[0]} | LONG | Anchor={anchorPrice} Current={currentPrice} Dist={distanceTicks:F2} ticks Qty={qty} (Anchor {(anchorUsesPreviousDataDay ? "PREV_DATA_DAY" : "HOY")} {anchorTime})");
                }
                else
                {
                    lastSignalText = "Anchor = Check, no trade";
                    Print($"{Time[0]} | AnchorPrice == CurrentPrice. No trade.");
                }

                RefreshStatusPanel();
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
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    if (double.IsNaN(highestPriceSinceEntry) || price > highestPriceSinceEntry)
                        highestPriceSinceEntry = price;
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    if (double.IsNaN(lowestPriceSinceEntry) || price < lowestPriceSinceEntry)
                        lowestPriceSinceEntry = price;
                }

                if (orderName.IndexOf("Profit", StringComparison.OrdinalIgnoreCase) >= 0)
                    tradeOutcomeText = "TP hit";
                else if (orderName.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                    tradeOutcomeText = trailingArmed ? "Trailing SL hit" : "SL hit";
                else if (marketPosition == MarketPosition.Flat && tradedToday)
                    tradeOutcomeText = "Position closed";
            }

            if (Position.MarketPosition == MarketPosition.Flat)
                ResetTrailingState();

            RefreshStatusPanel();
        }

        // ====== Trailing stop ======

        private void ManageTrailingStop()
        {
            if (!UseTrailingStop)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ResetTrailingState();
                return;
            }

            double avgPrice = Position.AveragePrice;
            if (Math.Abs(avgPrice) < double.Epsilon)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (double.IsNaN(highestPriceSinceEntry))
                    highestPriceSinceEntry = avgPrice;

                if (High[0] > highestPriceSinceEntry)
                    highestPriceSinceEntry = High[0];

                double favorableTicks = (highestPriceSinceEntry - avgPrice) / TickSize;
                if (favorableTicks < TrailingTriggerTicks)
                    return;

                trailingArmed = true;

                double extraTicks = favorableTicks - TrailingTriggerTicks;
                double stepCount = Math.Floor(extraTicks / TrailingStepTicks);
                double lockedDistanceTicks = TrailingDistanceTicks + stepCount * TrailingStepTicks;
                double desiredStopPrice = avgPrice + lockedDistanceTicks * TickSize;
                desiredStopPrice = Instrument.MasterInstrument.RoundToTickSize(desiredStopPrice);

                if (double.IsNaN(currentTrailingStopPrice) || desiredStopPrice > currentTrailingStopPrice)
                {
                    currentTrailingStopPrice = desiredStopPrice;
                    SetStopLoss("AnchorLong", CalculationMode.Price, currentTrailingStopPrice, false);
                    lastSignalText = $"LONG trailing active ({TrailingTriggerTicks}/{TrailingDistanceTicks}/{TrailingStepTicks})";
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (double.IsNaN(lowestPriceSinceEntry))
                    lowestPriceSinceEntry = avgPrice;

                if (Low[0] < lowestPriceSinceEntry)
                    lowestPriceSinceEntry = Low[0];

                double favorableTicks = (avgPrice - lowestPriceSinceEntry) / TickSize;
                if (favorableTicks < TrailingTriggerTicks)
                    return;

                trailingArmed = true;

                double extraTicks = favorableTicks - TrailingTriggerTicks;
                double stepCount = Math.Floor(extraTicks / TrailingStepTicks);
                double lockedDistanceTicks = TrailingDistanceTicks + stepCount * TrailingStepTicks;
                double desiredStopPrice = avgPrice - lockedDistanceTicks * TickSize;
                desiredStopPrice = Instrument.MasterInstrument.RoundToTickSize(desiredStopPrice);

                if (double.IsNaN(currentTrailingStopPrice) || desiredStopPrice < currentTrailingStopPrice)
                {
                    currentTrailingStopPrice = desiredStopPrice;
                    SetStopLoss("AnchorShort", CalculationMode.Price, currentTrailingStopPrice, false);
                    lastSignalText = $"SHORT trailing active ({TrailingTriggerTicks}/{TrailingDistanceTicks}/{TrailingStepTicks})";
                }
            }
        }

        private void ResetTrailingState()
        {
            highestPriceSinceEntry = double.NaN;
            lowestPriceSinceEntry = double.NaN;
            currentTrailingStopPrice = double.NaN;
            trailingArmed = false;

            SetStopLoss("AnchorLong", CalculationMode.Ticks, StopLossTicks, false);
            SetStopLoss("AnchorShort", CalculationMode.Ticks, StopLossTicks, false);
        }

        // ====== Anchor resolver ======

        private bool TryResolveAnchorPrice(DateTime checkBarTime, out double resolvedAnchorPrice, out string error)
        {
            resolvedAnchorPrice = double.NaN;
            error = null;

            DateTime checkDay = checkBarTime.Date;

            DateTime anchorDay;
            if (anchorUsesPreviousDataDay)
            {
                if (!TryGetPreviousDayWithData(checkDay, out anchorDay, out string prevErr))
                {
                    error = prevErr;
                    return false;
                }
            }
            else
            {
                anchorDay = checkDay;
            }

            DateTime targetAnchorDateTime = anchorDay.Add(anchorTime);

            int barsAgo = FindFirstBarAtOrAfter(targetAnchorDateTime);
            if (barsAgo < 0)
            {
                error = $"No hay bar disponible en/tras {targetAnchorDateTime:yyyy-MM-dd HH:mm:ss}.";
                return false;
            }

            if (Time[barsAgo].Date != anchorDay)
            {
                error = $"El bar encontrado cae en {Time[barsAgo]:yyyy-MM-dd HH:mm:ss} y no en el día esperado {anchorDay:yyyy-MM-dd}.";
                return false;
            }

            resolvedAnchorPrice = Close[barsAgo];
            return true;
        }

        private bool TryResolveAnchorPoint(DateTime checkBarTime, out double resolvedAnchorPrice, out DateTime resolvedAnchorTime, out string error)
        {
            resolvedAnchorPrice = double.NaN;
            resolvedAnchorTime  = DateTime.MinValue;
            error = null;

            DateTime checkDay = checkBarTime.Date;

            DateTime anchorDay;
            if (anchorUsesPreviousDataDay)
            {
                if (!TryGetPreviousDayWithData(checkDay, out anchorDay, out string prevErr))
                {
                    error = prevErr;
                    return false;
                }
            }
            else
            {
                anchorDay = checkDay;
            }

            DateTime targetAnchorDateTime = anchorDay.Add(anchorTime);

            int barsAgo = FindFirstBarAtOrAfter(targetAnchorDateTime);
            if (barsAgo < 0)
            {
                error = $"No hay bar disponible en/tras {targetAnchorDateTime:yyyy-MM-dd HH:mm:ss}.";
                return false;
            }

            if (Time[barsAgo].Date != anchorDay)
            {
                error = $"El bar encontrado cae en {Time[barsAgo]:yyyy-MM-dd HH:mm:ss} y no en el día esperado {anchorDay:yyyy-MM-dd}.";
                return false;
            }

            resolvedAnchorPrice = Close[barsAgo];
            resolvedAnchorTime  = Time[barsAgo];
            return true;
        }

        private bool TryGetPreviousDayWithData(DateTime referenceDay, out DateTime previousDayWithData, out string error)
        {
            previousDayWithData = DateTime.MinValue;
            error = null;

            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                DateTime d = Time[barsAgo].Date;
                if (d < referenceDay)
                {
                    previousDayWithData = d;
                    return true;
                }
            }

            error = $"No se encontró ningún día con datos anterior a {referenceDay:yyyy-MM-dd}.";
            return false;
        }

        private int FindFirstBarAtOrAfter(DateTime targetDateTime)
        {
            if (Time[0] < targetDateTime)
                return -1;

            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                DateTime t = Time[barsAgo];

                if (t < targetDateTime)
                {
                    int prev = barsAgo - 1;
                    return (prev >= 0) ? prev : -1;
                }
            }

            return CurrentBar;
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

        private void UpdateAnchorSegment(DateTime newAnchorTime, double newAnchorPrice)
        {
            if (ChartControl == null)
                return;

            int newAnchorBarsAgo = GetBarsAgoFromTime(newAnchorTime);

            if (currentAnchorSegmentStartTime == DateTime.MinValue || double.IsNaN(currentAnchorSegmentPrice))
            {
                anchorSegmentCounter++;
                currentAnchorSegmentStartTime = newAnchorTime;
                currentAnchorSegmentPrice     = newAnchorPrice;
                currentAnchorSegmentTag       = $"AnchorSegment_{anchorSegmentCounter}";

                Draw.Line(this, currentAnchorSegmentTag, false,
                    newAnchorBarsAgo, currentAnchorSegmentPrice,
                    0, currentAnchorSegmentPrice,
                    Brushes.DeepSkyBlue, DashStyleHelper.Solid, 2);

                return;
            }

            bool isDifferentAnchor =
                newAnchorTime != currentAnchorSegmentStartTime ||
                Math.Abs(newAnchorPrice - currentAnchorSegmentPrice) > TickSize * 0.5;

            if (isDifferentAnchor)
            {
                int previousStartBarsAgo = GetBarsAgoFromTime(currentAnchorSegmentStartTime);

                Draw.Line(this, currentAnchorSegmentTag, false,
                    previousStartBarsAgo, currentAnchorSegmentPrice,
                    newAnchorBarsAgo, currentAnchorSegmentPrice,
                    Brushes.DeepSkyBlue, DashStyleHelper.Solid, 2);

                anchorSegmentCounter++;
                currentAnchorSegmentStartTime = newAnchorTime;
                currentAnchorSegmentPrice     = newAnchorPrice;
                currentAnchorSegmentTag       = $"AnchorSegment_{anchorSegmentCounter}";

                Draw.Line(this, currentAnchorSegmentTag, false,
                    newAnchorBarsAgo, currentAnchorSegmentPrice,
                    0, currentAnchorSegmentPrice,
                    Brushes.DeepSkyBlue, DashStyleHelper.Solid, 2);
            }
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

        // ====== Panel visual funcional ======

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

                    tbBotName  = MakeTextBlock("", Brushes.DeepSkyBlue, FontWeights.Bold, PanelFontSize + 1);
                    tbLicense  = MakeTextBlock("", Brushes.LimeGreen, FontWeights.Normal, PanelFontSize);
                    tbExpiry   = MakeTextBlock("", Brushes.Gold, FontWeights.Normal, PanelFontSize);

                    sep1       = MakeSeparator();

                    tbAnchor   = MakeTextBlock("", PanelTextColor, FontWeights.Normal, PanelFontSize);
                    tbCheck    = MakeTextBlock("", PanelTextColor, FontWeights.Normal, PanelFontSize);
                    tbDistance = MakeTextBlock("", PanelTextColor, FontWeights.Normal, PanelFontSize);
                    tbMinTicks = MakeTextBlock("", Brushes.Gold, FontWeights.Bold, PanelFontSize);

                    sep2       = MakeSeparator();

                    tbSignal   = MakeTextBlock("", Brushes.DeepSkyBlue, FontWeights.Normal, PanelFontSize);
                    tbResult   = MakeTextBlock("", Brushes.Gainsboro, FontWeights.Bold, PanelFontSize);

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

                    chartTraderGrid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = GridLength.Auto
                    });

                    Grid.SetRow(statusPanelBorder, targetRow);
                    Grid.SetColumn(statusPanelBorder, 0);

                    System.Windows.Controls.Panel.SetZIndex(statusPanelBorder, 99999);
                    chartTraderGrid.Children.Add(statusPanelBorder);

                    RefreshStatusPanel();
                }
                catch (Exception ex)
                {
                    Print("Error creating visual panel in Chart Trader: " + ex.Message);
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
                    Print("Error removing visual panel: " + ex.Message);
                }
                finally
                {
                    statusPanelBorder = null;
                    statusPanelStack  = null;
                    chartWindow       = null;
                    chartTrader       = null;
                    chartTraderGrid   = null;

                    tbBotName  = null;
                    tbLicense  = null;
                    tbExpiry   = null;
                    tbAnchor   = null;
                    tbCheck    = null;
                    tbDistance = null;
                    tbMinTicks = null;
                    tbSignal   = null;
                    tbResult   = null;
                    sep1       = null;
                    sep2       = null;
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

                    string anchorText   = double.IsNaN(anchorPrice) ? "--" : FormatPrice(anchorPrice);
                    string checkText    = double.IsNaN(checkPrice) ? "--" : FormatPrice(checkPrice);
                    string distanceText = double.IsNaN(lastDistanceTicks) ? "--" : lastDistanceTicks.ToString("F2");

                    string minDistanceStatusText = !minDistanceMet.HasValue
                        ? "Pending"
                        : (minDistanceMet.Value ? "Met" : "Not met");

                    tbBotName.Text = $"{Name}";
                    tbBotName.FontFamily = new FontFamily(PanelFontFamily);
                    tbBotName.FontSize   = PanelFontSize + 1;
                    tbBotName.Foreground = Brushes.DeepSkyBlue;

                    tbLicense.Text = $"License: {licenseStatusText}";
                    tbLicense.FontFamily = new FontFamily(PanelFontFamily);
                    tbLicense.FontSize   = PanelFontSize;
                    tbLicense.Foreground = (licenseStatusText == "EXPIRADA" || licenseStatusText == "INVALIDA") ? Brushes.Red : Brushes.LimeGreen;

                    tbExpiry.Text = $"Expires: {ExpirationDate:dd/MM/yyyy}";
                    tbExpiry.FontFamily = new FontFamily(PanelFontFamily);
                    tbExpiry.FontSize   = PanelFontSize;
                    tbExpiry.Foreground = Brushes.Gold;

                    tbAnchor.Text = $"Anchor Price: {anchorText}";
                    tbAnchor.FontFamily = new FontFamily(PanelFontFamily);
                    tbAnchor.FontSize   = PanelFontSize;
                    tbAnchor.Foreground = PanelTextColor;

                    tbCheck.Text = $"Check Price: {checkText}";
                    tbCheck.FontFamily = new FontFamily(PanelFontFamily);
                    tbCheck.FontSize   = PanelFontSize;
                    tbCheck.Foreground = PanelTextColor;

                    tbDistance.Text = $"Distance: {distanceText} ticks";
                    tbDistance.FontFamily = new FontFamily(PanelFontFamily);
                    tbDistance.FontSize   = PanelFontSize;
                    tbDistance.Foreground = PanelTextColor;

                    tbMinTicks.Text = $"Min Dist ({MinDistanceTicks}): {minDistanceStatusText}";
                    tbMinTicks.FontFamily = new FontFamily(PanelFontFamily);
                    tbMinTicks.FontSize   = PanelFontSize;
                    tbMinTicks.Foreground = !minDistanceMet.HasValue
                        ? Brushes.Gold
                        : (minDistanceMet.Value ? Brushes.LimeGreen : Brushes.Red);

                    tbSignal.Text = $"Signal: {lastSignalText}";
                    tbSignal.FontFamily = new FontFamily(PanelFontFamily);
                    tbSignal.FontSize   = PanelFontSize;
                    tbSignal.Foreground = Brushes.DeepSkyBlue;

                    tbResult.Text = $"Result: {tradeOutcomeText}";
                    tbResult.FontFamily = new FontFamily(PanelFontFamily);
                    tbResult.FontSize   = PanelFontSize;
                    tbResult.Foreground =
                        tradeOutcomeText.Contains("TP") ? Brushes.LimeGreen :
                        tradeOutcomeText.Contains("SL") ? Brushes.Red :
                        Brushes.Gainsboro;

                    statusPanelBorder.Background = PanelBackgroundColor;
                }
                catch (Exception ex)
                {
                    Print("Error refreshing visual panel: " + ex.Message);
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

        // ====== Helpers ======

        private bool TryParseQuarterHourDecimal(double hourDecimal, out TimeSpan time, out string error)
        {
            time = TimeSpan.Zero;
            error = null;

            if (hourDecimal < 0 || hourDecimal > 23.75)
            {
                error = $"Debe estar entre 0 y 23.75. Recibido: {hourDecimal}";
                return false;
            }

            double quarters = hourDecimal / 0.25;
            double roundedQuarters = Math.Round(quarters);
            if (Math.Abs(quarters - roundedQuarters) > 1e-9)
            {
                error = $"Debe ser múltiplo de 0.25. Recibido: {hourDecimal}";
                return false;
            }

            int totalMinutes = (int)Math.Round(hourDecimal * 60.0);
            int hh = totalMinutes / 60;
            int mm = totalMinutes % 60;

            if (hh < 0 || hh > 23 || (mm != 0 && mm != 15 && mm != 30 && mm != 45))
            {
                error = $"Formato inválido tras conversión. Recibido: {hourDecimal} => {hh:D2}:{mm:D2}";
                return false;
            }

            time = new TimeSpan(hh, mm, 0);
            return true;
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
                    return machineId.Trim().Replace(" ", string.Empty).ToUpperInvariant();

                Type globalsType = typeof(NinjaTrader.Core.Globals);

                var prop = globalsType.GetProperty("MachineId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (prop != null)
                {
                    object value = prop.GetValue(null, null);
                    machineId = value == null ? string.Empty : value.ToString();
                    if (!string.IsNullOrWhiteSpace(machineId))
                        return machineId.Trim().Replace(" ", string.Empty).ToUpperInvariant();
                }

                var field = globalsType.GetField("MachineId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (field != null)
                {
                    object value = field.GetValue(null);
                    machineId = value == null ? string.Empty : value.ToString();
                    if (!string.IsNullOrWhiteSpace(machineId))
                        return machineId.Trim().Replace(" ", string.Empty).ToUpperInvariant();
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
            return !string.IsNullOrWhiteSpace(currentMachineId)
                && string.Equals(currentMachineId, AllowedMachineId, StringComparison.OrdinalIgnoreCase);
        }

        private int ToDay(DateTime time)
        {
            return time.Year * 10000 + time.Month * 100 + time.Day;
        }
    }
}
