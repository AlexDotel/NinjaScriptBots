#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AnchorFvgSessionBot : Strategy
    {
        public enum ConfirmationFilterMode
        {
            None,
            RSI,
            EMA,
            RSIOrEMA,
            RSIAndEMA
        }

        private const string LongSignalName = "AnchorFvgLong";
        private const string ShortSignalName = "AnchorFvgShort";

        private EMA ema;
        private RSI rsi;
        private ADX adx;

        private int checkMinutes;
        private int endMinutes;
        private TimeSpan anchorTime;
        private bool anchorUsesPreviousDataDay;

        private DateTime currentTradingDate;
        private bool tradingDateInitialized;
        private bool biasCheckedToday;
        private bool tradeSubmittedToday;
        private TradeDirection dailyBias;
        private double activeAnchorPrice;
        private double activeCheckPrice;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Opera FVG dentro de una ventana horaria usando bias por Anchor/Check, distancia minima, filtros RSI/EMA y ADX en expansion.";
                Name = "AnchorFvgSessionBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 3;
                DefaultQuantity = 1;

                EntryQuantity = 1;
                EnableLongs = true;
                EnableShorts = true;

                CheckHour = 9.00;
                EndHour = 11.00;
                AnchorHour = 22.00;
                MinAnchorDistanceTicks = 10;

                MinFvgTicks = 2;
                RequireThreeSameColorCandles = true;

                ConfirmationFilter = ConfirmationFilterMode.RSIOrEMA;
                RsiPeriod = 14;
                RsiSmooth = 3;
                RsiLongLevel = 50.0;
                EmaPeriod = 50;

                UseAdxFilter = true;
                AdxPeriod = 14;
                MinAdxLevel = 20.0;
                AdxLookbackBars = 1;
                AdxExpansionMultiplier = 1.0;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();

                checkMinutes = ConvertQuarterHourToMinutes(CheckHour);
                endMinutes = ConvertQuarterHourToMinutes(EndHour);
                anchorTime = ConvertQuarterHourDecimalToTimeSpan(AnchorHour, nameof(AnchorHour));
                anchorUsesPreviousDataDay = ConvertTimeSpanToMinutes(anchorTime) > checkMinutes;

                ResetRuntimeState();
            }
            else if (State == State.DataLoaded)
            {
                ema = EMA(EmaPeriod);
                rsi = RSI(RsiPeriod, RsiSmooth);
                adx = ADX(AdxPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < GetRequiredBars())
                return;

            ResetDailyStateIfNeeded();

            if (!biasCheckedToday && HasCrossedTime(checkMinutes))
                ResolveDailyBias();

            if (!biasCheckedToday || dailyBias == TradeDirection.None || tradeSubmittedToday)
                return;

            if (!IsWithinSetupWindow())
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (dailyBias == TradeDirection.Long)
            {
                if (!EnableLongs || !HasBullishFvg() || !PassesConfirmationFilters(TradeDirection.Long) || !PassesAdxFilter())
                    return;

                SubmitEntry(TradeDirection.Long);
                return;
            }

            if (!EnableShorts || !HasBearishFvg() || !PassesConfirmationFilters(TradeDirection.Short) || !PassesAdxFilter())
                return;

            SubmitEntry(TradeDirection.Short);
        }

        private void SubmitEntry(TradeDirection direction)
        {
            string signalName = direction == TradeDirection.Long ? LongSignalName : ShortSignalName;
            SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, ProfitTargetTicks);

            if (direction == TradeDirection.Long)
                EnterLong(EntryQuantity, signalName);
            else
                EnterShort(EntryQuantity, signalName);

            tradeSubmittedToday = true;

            Print(string.Format(
                "{0} | {1} FVG enviado. Anchor={2} Check={3} DistTicks={4:F2} FvgTicks={5:F2} ADX={6:F2}",
                Time[0],
                direction,
                activeAnchorPrice,
                activeCheckPrice,
                Math.Abs(activeAnchorPrice - activeCheckPrice) / TickSize,
                GetCurrentFvgTicks(direction),
                adx[0]));
        }

        private void ResolveDailyBias()
        {
            biasCheckedToday = true;
            dailyBias = TradeDirection.None;
            activeAnchorPrice = double.NaN;
            activeCheckPrice = Close[0];

            if (!TryResolveAnchorPoint(Time[0], out activeAnchorPrice, out DateTime resolvedAnchorTime, out string error))
            {
                Print(string.Format("{0} | Bias no resuelto: {1}", Time[0], error));
                return;
            }

            double distanceTicks = Math.Abs(activeCheckPrice - activeAnchorPrice) / TickSize;
            if (distanceTicks < MinAnchorDistanceTicks)
            {
                Print(string.Format(
                    "{0} | Bias omitido por distancia insuficiente. Anchor={1} Check={2} DistTicks={3:F2} MinTicks={4}",
                    Time[0],
                    activeAnchorPrice,
                    activeCheckPrice,
                    distanceTicks,
                    MinAnchorDistanceTicks));
                return;
            }

            if (activeAnchorPrice > activeCheckPrice)
                dailyBias = TradeDirection.Long;
            else if (activeAnchorPrice < activeCheckPrice)
                dailyBias = TradeDirection.Short;

            if ((dailyBias == TradeDirection.Long && !EnableLongs) || (dailyBias == TradeDirection.Short && !EnableShorts))
                dailyBias = TradeDirection.None;

            Print(string.Format(
                "{0} | Bias {1}. Anchor={2} Check={3} DistTicks={4:F2} AnchorBar={5:yyyy-MM-dd HH:mm:ss}",
                Time[0],
                dailyBias,
                activeAnchorPrice,
                activeCheckPrice,
                distanceTicks,
                resolvedAnchorTime));
        }

        private bool HasBullishFvg()
        {
            if (Low[0] <= High[2])
                return false;

            if (GetCurrentFvgTicks(TradeDirection.Long) < MinFvgTicks)
                return false;

            return !RequireThreeSameColorCandles || (IsBullishBar(0) && IsBullishBar(1) && IsBullishBar(2));
        }

        private bool HasBearishFvg()
        {
            if (High[0] >= Low[2])
                return false;

            if (GetCurrentFvgTicks(TradeDirection.Short) < MinFvgTicks)
                return false;

            return !RequireThreeSameColorCandles || (IsBearishBar(0) && IsBearishBar(1) && IsBearishBar(2));
        }

        private double GetCurrentFvgTicks(TradeDirection direction)
        {
            if (direction == TradeDirection.Long)
                return Math.Max(0.0, (Low[0] - High[2]) / TickSize);

            if (direction == TradeDirection.Short)
                return Math.Max(0.0, (Low[2] - High[0]) / TickSize);

            return 0.0;
        }

        private bool PassesConfirmationFilters(TradeDirection direction)
        {
            if (ConfirmationFilter == ConfirmationFilterMode.None)
                return true;

            bool rsiPassed = PassesRsiFilter(direction);
            bool emaPassed = PassesEmaFilter(direction);

            if (ConfirmationFilter == ConfirmationFilterMode.RSI)
                return rsiPassed;

            if (ConfirmationFilter == ConfirmationFilterMode.EMA)
                return emaPassed;

            if (ConfirmationFilter == ConfirmationFilterMode.RSIAndEMA)
                return rsiPassed && emaPassed;

            return rsiPassed || emaPassed;
        }

        private bool PassesRsiFilter(TradeDirection direction)
        {
            if (direction == TradeDirection.Long)
                return rsi[0] >= RsiLongLevel;

            return rsi[0] <= 100.0 - RsiLongLevel;
        }

        private bool PassesEmaFilter(TradeDirection direction)
        {
            if (direction == TradeDirection.Long)
                return Close[0] >= ema[0];

            return Close[0] <= ema[0];
        }

        private bool PassesAdxFilter()
        {
            if (!UseAdxFilter)
                return true;

            if (adx[0] < MinAdxLevel)
                return false;

            return adx[0] >= adx[AdxLookbackBars] * AdxExpansionMultiplier;
        }

        private bool TryResolveAnchorPoint(DateTime checkBarTime, out double resolvedAnchorPrice, out DateTime resolvedAnchorTime, out string error)
        {
            resolvedAnchorPrice = double.NaN;
            resolvedAnchorTime = DateTime.MinValue;
            error = null;

            DateTime checkDay = checkBarTime.Date;
            DateTime anchorDay = checkDay;

            if (anchorUsesPreviousDataDay && !TryGetPreviousDayWithData(checkDay, out anchorDay, out error))
                return false;

            DateTime targetAnchorDateTime = anchorDay.Add(anchorTime);
            int barsAgo = FindFirstBarAtOrAfter(targetAnchorDateTime);

            if (barsAgo < 0)
            {
                error = string.Format("No hay barra disponible en o despues de {0:yyyy-MM-dd HH:mm:ss}.", targetAnchorDateTime);
                return false;
            }

            if (Time[barsAgo].Date != anchorDay)
            {
                error = string.Format(
                    "La barra anchor resuelta cae en {0:yyyy-MM-dd HH:mm:ss} y no en el dia esperado {1:yyyy-MM-dd}.",
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

        private void ResetRuntimeState()
        {
            currentTradingDate = DateTime.MinValue;
            tradingDateInitialized = false;
            biasCheckedToday = false;
            tradeSubmittedToday = false;
            dailyBias = TradeDirection.None;
            activeAnchorPrice = double.NaN;
            activeCheckPrice = double.NaN;
        }

        private void ResetDailyStateIfNeeded()
        {
            DateTime barDate = Time[0].Date;

            if (tradingDateInitialized && currentTradingDate == barDate)
                return;

            currentTradingDate = barDate;
            tradingDateInitialized = true;
            biasCheckedToday = false;
            tradeSubmittedToday = false;
            dailyBias = TradeDirection.None;
            activeAnchorPrice = double.NaN;
            activeCheckPrice = double.NaN;
        }

        private bool HasCrossedTime(int targetMinutes)
        {
            int currentMinutes = ConvertDateTimeToMinutes(Time[0]);
            if (currentMinutes < targetMinutes)
                return false;

            if (CurrentBar == 0 || Time[1].Date != Time[0].Date)
                return true;

            return ConvertDateTimeToMinutes(Time[1]) < targetMinutes;
        }

        private bool IsWithinSetupWindow()
        {
            int currentMinutes = ConvertDateTimeToMinutes(Time[0]);

            if (checkMinutes == endMinutes)
                return currentMinutes >= checkMinutes;

            if (checkMinutes < endMinutes)
                return currentMinutes >= checkMinutes && currentMinutes <= endMinutes;

            return currentMinutes >= checkMinutes || currentMinutes <= endMinutes;
        }

        private int GetRequiredBars()
        {
            int indicatorBars = Math.Max(EmaPeriod, Math.Max(RsiPeriod + RsiSmooth, AdxPeriod + AdxLookbackBars));
            return Math.Max(2, indicatorBars);
        }

        private bool IsBullishBar(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsBearishBar(int barsAgo)
        {
            return Close[barsAgo] < Open[barsAgo];
        }

        private void ValidateConfiguration()
        {
            if (EntryQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(EntryQuantity), "EntryQuantity debe ser mayor o igual que 1.");

            if (!EnableLongs && !EnableShorts)
                throw new ArgumentException("Debes habilitar compras, ventas o ambas.");

            ValidateQuarterHourInput(CheckHour, nameof(CheckHour));
            ValidateQuarterHourInput(EndHour, nameof(EndHour));
            ValidateQuarterHourInput(AnchorHour, nameof(AnchorHour));

            if (MinAnchorDistanceTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(MinAnchorDistanceTicks), "MinAnchorDistanceTicks no puede ser negativo.");

            if (MinFvgTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(MinFvgTicks), "MinFvgTicks no puede ser negativo.");

            if (RsiPeriod <= 0)
                throw new ArgumentOutOfRangeException(nameof(RsiPeriod), "RsiPeriod debe ser mayor que 0.");

            if (RsiSmooth <= 0)
                throw new ArgumentOutOfRangeException(nameof(RsiSmooth), "RsiSmooth debe ser mayor que 0.");

            if (RsiLongLevel < 0 || RsiLongLevel > 100)
                throw new ArgumentOutOfRangeException(nameof(RsiLongLevel), "RsiLongLevel debe estar entre 0 y 100.");

            if (EmaPeriod <= 0)
                throw new ArgumentOutOfRangeException(nameof(EmaPeriod), "EmaPeriod debe ser mayor que 0.");

            if (AdxPeriod <= 0)
                throw new ArgumentOutOfRangeException(nameof(AdxPeriod), "AdxPeriod debe ser mayor que 0.");

            if (MinAdxLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(MinAdxLevel), "MinAdxLevel no puede ser negativo.");

            if (AdxLookbackBars <= 0)
                throw new ArgumentOutOfRangeException(nameof(AdxLookbackBars), "AdxLookbackBars debe ser mayor que 0.");

            if (AdxExpansionMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(AdxExpansionMultiplier), "AdxExpansionMultiplier debe ser mayor que 0.");

            if (StopLossTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(StopLossTicks), "StopLossTicks debe ser mayor que 0.");

            if (ProfitTargetTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(ProfitTargetTicks), "ProfitTargetTicks debe ser mayor que 0.");
        }

        private void ValidateQuarterHourInput(double value, string parameterName)
        {
            if (value < 0 || value > 23.75)
                throw new ArgumentOutOfRangeException(parameterName, parameterName + " debe estar entre 0.00 y 23.75.");

            double quarterValue = value * 4.0;
            if (Math.Abs(quarterValue - Math.Round(quarterValue)) > 0.0001)
                throw new ArgumentException(parameterName + " solo acepta incrementos de 0.25. Ejemplos validos: 9.00, 9.25, 9.50, 9.75.", parameterName);
        }

        private TimeSpan ConvertQuarterHourDecimalToTimeSpan(double hourDecimal, string parameterName)
        {
            ValidateQuarterHourInput(hourDecimal, parameterName);

            int totalMinutes = ConvertQuarterHourToMinutes(hourDecimal);
            return new TimeSpan(totalMinutes / 60, totalMinutes % 60, 0);
        }

        private int ConvertQuarterHourToMinutes(double value)
        {
            return (int)Math.Round(value * 4.0) * 15;
        }

        private int ConvertTimeSpanToMinutes(TimeSpan time)
        {
            return (time.Hours * 60) + time.Minutes;
        }

        private int ConvertDateTimeToMinutes(DateTime time)
        {
            return (time.Hour * 60) + time.Minute;
        }

        private enum TradeDirection
        {
            None,
            Long,
            Short
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
        [Range(0.0, 23.75)]
        [Display(Name = "Hora check/inicio", Description = "Hora donde se calcula el bias. Formato en cuartos: 9.50 = 9:30.", GroupName = "02. Horario Anchor", Order = 0)]
        public double CheckHour
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora fin setups", Description = "Ultima hora permitida para buscar FVG.", GroupName = "02. Horario Anchor", Order = 1)]
        public double EndHour
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 23.75)]
        [Display(Name = "Hora anchor", Description = "Si es mayor que la hora check, se busca en el dia previo con datos.", GroupName = "02. Horario Anchor", Order = 2)]
        public double AnchorHour
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Distancia minima anchor (ticks)", GroupName = "02. Horario Anchor", Order = 3)]
        public int MinAnchorDistanceTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "FVG minimo (ticks)", GroupName = "03. Trigger FVG", Order = 0)]
        public int MinFvgTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exigir 3 velas del color", GroupName = "03. Trigger FVG", Order = 1)]
        public bool RequireThreeSameColorCandles
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro confirmacion", GroupName = "04. RSI / EMA", Order = 0)]
        public ConfirmationFilterMode ConfirmationFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "RSI periodo", GroupName = "04. RSI / EMA", Order = 1)]
        public int RsiPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "RSI suavizado", GroupName = "04. RSI / EMA", Order = 2)]
        public int RsiSmooth
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "RSI nivel long", Description = "Long requiere RSI >= nivel. Short usa el nivel simetrico: RSI <= 100 - nivel.", GroupName = "04. RSI / EMA", Order = 3)]
        public double RsiLongLevel
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA periodo", GroupName = "04. RSI / EMA", Order = 4)]
        public int EmaPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro ADX", GroupName = "05. ADX", Order = 0)]
        public bool UseAdxFilter
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX periodo", GroupName = "05. ADX", Order = 1)]
        public int AdxPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX minimo", GroupName = "05. ADX", Order = 2)]
        public double MinAdxLevel
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX lookback velas", GroupName = "05. ADX", Order = 3)]
        public int AdxLookbackBars
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "ADX multiplicador expansion", Description = "1.0 exige ADX actual >= ADX del lookback. 2.0 exige el doble, 3.0 el triple.", GroupName = "05. ADX", Order = 4)]
        public double AdxExpansionMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", GroupName = "06. Riesgo", Order = 0)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take profit (ticks)", GroupName = "06. Riesgo", Order = 1)]
        public int ProfitTargetTicks
        { get; set; }
    }
}
