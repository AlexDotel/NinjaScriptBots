#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class BOrigin : Strategy
    {
        // ===== Indicadores =====
        private ADX adx;
        private EMA trendEma;

        // ===== Control diario =====
        private int lastSessionDate = -1;
        private bool tradedToday = false;
        private DateTime currentEstDate = DateTime.MinValue;
        private double dailyCumProfitAtStart = 0;
        private bool dailyLimitReached = false;
        private TimeZoneInfo easternTimeZone;
        private int lastProcessedTradeCount = 0;
        private int consecutiveLosingTrades = 0;
        private bool hasProcessedBars = false;
        private bool csvExported = false;
        private int lastBreakoutBar = -1;
        private readonly DateTime expirationDate = new DateTime(2026, 6, 30);

        #region Inputs

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "Breakout Lookback (N)", Order = 1, GroupName = "1. Breakout")]
        public int Lookback { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Allow Long", Order = 2, GroupName = "1. Breakout")]
        public bool AllowLong { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Allow Short", Order = 3, GroupName = "1. Breakout")]
        public bool AllowShort { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "One trade per day", Order = 4, GroupName = "1. Breakout")]
        public bool OneTradePerDay { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "Breakout Cooldown Bars", Order = 5, GroupName = "1. Breakout")]
        public int BreakoutCooldownBars { get; set; } = 2;

        // ===== Risk: fixed SL ticks + RRR TP =====
        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Stop Loss (ticks)", Order = 10, GroupName = "2. Risk")]
        public int StopLossTicks { get; set; } = 50;

        [NinjaScriptProperty]
        [Range(0.1, 50)]
        [Display(Name = "RRR (TP = SL * RRR)", Order = 12, GroupName = "2. Risk")]
        public double Rrr { get; set; } = 0.25;

        [NinjaScriptProperty]
        [Display(Name = "Use Daily Profit Target", Order = 13, GroupName = "2. Risk")]
        public bool UseDailyProfitTarget { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Daily Profit Target ($)", Order = 14, GroupName = "2. Risk")]
        public double DailyProfitTargetDollars { get; set; } = 1000;

        [NinjaScriptProperty]
        [Display(Name = "Use Daily Loss Limit", Order = 15, GroupName = "2. Risk")]
        public bool UseDailyLossLimit { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Daily Loss Limit ($)", Order = 16, GroupName = "2. Risk")]
        public double DailyLossLimitDollars { get; set; } = 1000;

        // ===== Martingale =====
        [NinjaScriptProperty]
        [Display(Name = "Use Martingale", Order = 17, GroupName = "2. Risk")]
        public bool UseMartingale { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Martingale After Losses", Order = 18, GroupName = "2. Risk")]
        public int MartingaleAfterLosses { get; set; } = 4;

        [NinjaScriptProperty]
        [Range(1.0, 10.0)]
        [Display(Name = "Martingale Multiplier", Order = 19, GroupName = "2. Risk")]
        public double MartingaleMultiplier { get; set; } = 10.0;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Martingale Max Quantity", Order = 20, GroupName = "2. Risk")]
        public int MartingaleMaxQuantity { get; set; } = 10;

        // ===== Time filters =====
        [NinjaScriptProperty]
        [Display(Name = "Use Time Window", Order = 20, GroupName = "3. Time Filters")]
        public bool UseTimeWindow { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Start (HHmm)", Order = 21, GroupName = "3. Time Filters")]
        public int StartHHmm { get; set; } = 1800;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "End (HHmm)", Order = 22, GroupName = "3. Time Filters")]
        public int EndHHmm { get; set; } = 1645;

        [NinjaScriptProperty]
        [Display(Name = "Use Forced Close Time", Order = 23, GroupName = "3. Time Filters")]
        public bool UseCloseTime { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Close (HHmm)", Order = 24, GroupName = "3. Time Filters")]
        public int CloseHHmm { get; set; } = 1655;

        // ===== Trend filter (EMA) =====
        [NinjaScriptProperty]
        [Display(Name = "Use Trend EMA Filter", Order = 30, GroupName = "4. Optional Filters")]
        public bool UseTrendEma { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "Trend EMA Period (0 = off)", Order = 31, GroupName = "4. Optional Filters")]
        public int TrendEmaPeriod { get; set; } = 10;

        // ===== ADX filter =====
        [NinjaScriptProperty]
        [Display(Name = "Use ADX Filter", Order = 40, GroupName = "4. Optional Filters")]
        public bool UseAdxFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Order = 41, GroupName = "4. Optional Filters")]
        public int AdxPeriod { get; set; } = 14;

        // Requiere fuerza minima de tendencia: ADX >= AdxMin
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Min (Require ADX >= Min)", Order = 42, GroupName = "4. Optional Filters")]
        public double AdxMin { get; set; } = 50;

        // ===== Backtest export =====
        [NinjaScriptProperty]
        [Display(Name = "Export Backtest CSV", Order = 60, GroupName = "6. Backtest Export")]
        public bool ExportBacktestCsv { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "CSV Export Folder", Order = 61, GroupName = "6. Backtest Export")]
        public string CsvExportFolder { get; set; } = @"C:\NinjaTraderBacktests";

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BOrigin";
                Calculate = Calculate.OnBarClose;   // puedes cambiar a OnEachTick si quieres más intrabar
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false; // lo controlamos con CloseHHmm si se activa
                BarsRequiredToTrade = 60;
            }
            else if (State == State.Configure)
            {
                IncludeTradeHistoryInBacktest = true;
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                if (TrendEmaPeriod > 0)
                    trendEma = EMA(TrendEmaPeriod);
            }
            else if (State == State.Terminated)
            {
                ExportBacktestCsvIfNeeded();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(BarsRequiredToTrade, Lookback + 2))
                return;

            if (Time[0].Date > expirationDate)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("ExpiredExit", "LongBreakout");

                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("ExpiredExit", "ShortBreakout");

                return;
            }

            hasProcessedBars = true;

            DateTime estTime = ToEasternTime(Time[0]);
            int estNow = HHmmToIntTime((estTime.Hour * 100) + estTime.Minute);
            DateTime tradingDate = GetTradingDate(estTime, estNow);
            ProcessClosedTrades();

            // ===== Imprimir fecha por sesión (control backtest) =====
            if (tradingDate != currentEstDate)
            {
                currentEstDate = tradingDate;
                lastSessionDate = ToDay(tradingDate);
                tradedToday = false;
                dailyLimitReached = false;
                dailyCumProfitAtStart = GetTotalRealizedPnl();
            }

            double dailyRealizedPnl = GetDailyRealizedPnl();
            if (DailyLimitsReached(dailyRealizedPnl))
                dailyLimitReached = true;

            // ===== Forzar cierre por hora (opcional) =====
            if (UseCloseTime && IsForcedCloseTime(estNow))
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("CloseTimeExit", "LongBreakout");

                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("CloseTimeExit", "ShortBreakout");

                return;
            }

            // Si ya hay posicion, no reconfigurar SL/TP de entrada.
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                return;
            }

            // ===== Ventana horaria (opcional) =====
            if (UseTimeWindow)
            {
                int start = HHmmToIntTime(StartHHmm);
                int end = HHmmToIntTime(EndHHmm);

                // Si el usuario pone un rango que cruza medianoche, también funcionará:
                bool inWindow = (start <= end) ? (estNow >= start && estNow <= end) : (estNow >= start || estNow <= end);
                if (!inWindow)
                    return;
            }

            // ===== Tope diario en dinero realizado =====
            if (dailyLimitReached)
                return;

            // ===== 1 trade por dia (opcional) =====
            if (OneTradePerDay && tradedToday)
                return;

            // ===== Filtro ADX (opcional): requiere fuerza minima de tendencia =====
            if (UseAdxFilter)
            {
                if (adx[0] < AdxMin)
                    return;
            }

            // ===== Filtro de tendencia EMA (opcional) =====
            if (UseTrendEma && TrendEmaPeriod > 0 && trendEma != null)
            {
                // Para long: precio por encima de EMA; para short: por debajo
                // (Se aplica más abajo según dirección)
            }

            // ===== Niveles Breakout 55 (excluyendo vela actual) =====
            double prev55High = MAX(High, Lookback)[1];
            double prev55Low  = MIN(Low, Lookback)[1];

            // ===== Calculo de SL/TP con SL fijo en ticks =====
            int slTicks = StopLossTicks;
            slTicks = Math.Max(1, slTicks);

            int tpTicks = (int)Math.Round(slTicks * Rrr, MidpointRounding.AwayFromZero);
            tpTicks = Math.Max(1, tpTicks);

            // Configurar SL/TP (se aplican a la próxima entrada)
            SetStopLoss(CalculationMode.Ticks, slTicks);
            SetProfitTarget(CalculationMode.Ticks, tpTicks);

            // ===== Señales de entrada =====
            bool cooldownActive = lastBreakoutBar >= 0 && CurrentBar - lastBreakoutBar <= BreakoutCooldownBars;
            bool breakLong = !cooldownActive && AllowLong && Close[0] > prev55High && Close[1] <= prev55High;
            bool breakShort = !cooldownActive && AllowShort && Close[0] < prev55Low && Close[1] >= prev55Low;

            // Trend EMA gating (si está activo)
            if (UseTrendEma && TrendEmaPeriod > 0 && trendEma != null)
            {
                if (breakLong && Close[0] <= trendEma[0])
                    breakLong = false;

                if (breakShort && Close[0] >= trendEma[0])
                    breakShort = false;
            }

            // Evitar entrar si ya estamos en posición
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                return;
            }

            // ===== Ejecutar entradas (Market) =====
            int entryQuantity = GetEntryQuantity();
            if (breakLong)
            {
                EnterLong(entryQuantity, "LongBreakout");
                tradedToday = true;
                lastBreakoutBar = CurrentBar;
            }
            else if (breakShort)
            {
                EnterShort(entryQuantity, "ShortBreakout");
                tradedToday = true;
                lastBreakoutBar = CurrentBar;
            }

        }

        private DateTime GetTradingDate(DateTime estTime, int estNow)
        {
            if (!UseTimeWindow)
                return estTime.Date;

            int start = HHmmToIntTime(StartHHmm);
            int end = HHmmToIntTime(EndHHmm);

            if (start > end && estNow < start)
                return estTime.Date.AddDays(-1);

            return estTime.Date;
        }

        private bool IsForcedCloseTime(int estNow)
        {
            int close = HHmmToIntTime(CloseHHmm);

            if (!UseTimeWindow)
                return estNow >= close;

            int start = HHmmToIntTime(StartHHmm);
            int end = HHmmToIntTime(EndHHmm);

            if (start > end && close <= start)
                return estNow >= close && estNow < start;

            return estNow >= close;
        }

        private void ProcessClosedTrades()
        {
            int tradeCount = SystemPerformance.AllTrades.Count;

            if (lastProcessedTradeCount > tradeCount)
            {
                lastProcessedTradeCount = tradeCount;
                consecutiveLosingTrades = 0;
                return;
            }

            for (int i = lastProcessedTradeCount; i < tradeCount; i++)
            {
                double tradeProfit = SystemPerformance.AllTrades[i].ProfitCurrency;

                if (tradeProfit < 0)
                    consecutiveLosingTrades++;
                else if (tradeProfit > 0)
                    consecutiveLosingTrades = 0;
            }

            lastProcessedTradeCount = tradeCount;
        }

        private int GetEntryQuantity()
        {
            int baseQuantity = Math.Max(1, DefaultQuantity);

            if (!UseMartingale || consecutiveLosingTrades < MartingaleAfterLosses)
                return baseQuantity;

            int martingaleSteps = consecutiveLosingTrades - MartingaleAfterLosses + 1;
            int martingaleQuantity = (int)Math.Round(baseQuantity * Math.Pow(MartingaleMultiplier, martingaleSteps), MidpointRounding.AwayFromZero);
            int maxQuantity = Math.Max(baseQuantity, MartingaleMaxQuantity);

            return Math.Max(1, Math.Min(martingaleQuantity, maxQuantity));
        }

        private DateTime ToEasternTime(DateTime sourceTime)
        {
            if (easternTimeZone == null)
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            DateTime localTime = sourceTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(sourceTime, DateTimeKind.Local)
                : sourceTime;

            return TimeZoneInfo.ConvertTime(localTime, easternTimeZone);
        }

        private double GetTotalRealizedPnl()
        {
            return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
        }

        private double GetDailyRealizedPnl()
        {
            return GetTotalRealizedPnl() - dailyCumProfitAtStart;
        }

        private bool DailyLimitsReached(double dailyRealizedPnl)
        {
            if (UseDailyProfitTarget && DailyProfitTargetDollars > 0 && dailyRealizedPnl >= DailyProfitTargetDollars)
                return true;

            if (UseDailyLossLimit && DailyLossLimitDollars > 0 && dailyRealizedPnl <= -DailyLossLimitDollars)
                return true;

            return false;
        }

        private void ExportBacktestCsvIfNeeded()
        {
            if (!ExportBacktestCsv || csvExported || !hasProcessedBars)
                return;

            try
            {
                string folder = string.IsNullOrWhiteSpace(CsvExportFolder)
                    ? @"C:\NinjaTraderBacktests"
                    : CsvExportFolder.Trim();

                Directory.CreateDirectory(folder);

                string parameterTag = BuildCompressedParameterTag();
                string fileName = SanitizeFileName(parameterTag) + ".csv";
                string filePath = GetUniqueFilePath(Path.Combine(folder, fileName));

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("Trade number,Instrument,Account,Strategy,Market pos.,Qty,Entry price,Exit price,Entry time,Exit time,Entry name,Exit name,Profit,Cum. net profit,Commission,Clearing Fee,Exchange Fee,IP Fee,NFA Fee,MAE,MFE,ETD,Bars,");

                double cumProfit = 0;
                for (int i = 0; i < SystemPerformance.AllTrades.Count; i++)
                {
                    Trade trade = SystemPerformance.AllTrades[i];
                    cumProfit += trade.ProfitCurrency;
                    AppendTradeCsvRow(csv, i + 1, trade, cumProfit);
                }

                File.WriteAllText(filePath, csv.ToString());
                csvExported = true;
            }
            catch (Exception ex)
            {
                Print($"Backtest CSV export failed: {ex.Message}");
            }
        }

        private string BuildCompressedParameterTag()
        {
            string timeTag = UseTimeWindow
                ? $"TF{StartHHmm}-{EndHHmm}"
                : "NFT";

            string closeTag = UseCloseTime
                ? $"FC{CloseHHmm}"
                : "NFC";

            string trendTag = UseTrendEma && TrendEmaPeriod > 0
                ? $"EMA{TrendEmaPeriod}"
                : "NEMA";

            string adxTag = UseAdxFilter
                ? $"ADX{Fmt(AdxMin)}P{AdxPeriod}"
                : "NADX";

            string martingaleTag = UseMartingale
                ? $"MG1A{MartingaleAfterLosses}X{Fmt(MartingaleMultiplier)}M{MartingaleMaxQuantity}"
                : $"MG0X{Fmt(MartingaleMultiplier)}M{MartingaleMaxQuantity}";

            string dailyProfitTag = UseDailyProfitTarget
                ? $"DPT{Fmt(DailyProfitTargetDollars)}"
                : "NDPT";

            string dailyLossTag = UseDailyLossLimit
                ? $"DLL{Fmt(DailyLossLimitDollars)}"
                : "NDLL";

            return string.Join(" ",
                "BOrigin",
                $"BL{Lookback}",
                $"CD{BreakoutCooldownBars}",
                $"SL{StopLossTicks}",
                $"RRR{Fmt(Rrr)}",
                timeTag,
                closeTag,
                martingaleTag,
                trendTag,
                adxTag,
                dailyProfitTag,
                dailyLossTag);
        }

        private void AppendTradeCsvRow(StringBuilder csv, int tradeNumber, Trade trade, double cumProfit)
        {
            Execution entry = trade.Entry;
            Execution exit = trade.Exit;

            string direction = entry != null
                ? entry.MarketPosition.ToString()
                : string.Empty;

            double entryPrice = entry != null ? entry.Price : 0;
            double exitPrice = exit != null ? exit.Price : 0;
            double maeCurrency = Math.Abs(trade.MaeTicks) * TickValue() * trade.Quantity;
            double mfeCurrency = Math.Abs(trade.MfeTicks) * TickValue() * trade.Quantity;
            double etdCurrency = Math.Max(0, mfeCurrency - trade.ProfitCurrency);

            csv.Append(tradeNumber.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(GridText(Instrument != null ? Instrument.FullName : string.Empty)).Append(',');
            csv.Append("Backtest").Append(',');
            csv.Append(GridText(Name)).Append(',');
            csv.Append(GridText(direction)).Append(',');
            csv.Append(trade.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(FormatGridPrice(entryPrice)).Append(',');
            csv.Append(FormatGridPrice(exitPrice)).Append(',');
            csv.Append(entry != null ? FormatGridDateTime(entry.Time) : string.Empty).Append(',');
            csv.Append(exit != null ? FormatGridDateTime(exit.Time) : string.Empty).Append(',');
            csv.Append(GridText(entry != null ? entry.Name : string.Empty)).Append(',');
            csv.Append(GridText(exit != null ? exit.Name : string.Empty)).Append(',');
            csv.Append(FormatGridMoney(trade.ProfitCurrency)).Append(',');
            csv.Append(FormatGridMoney(cumProfit)).Append(',');
            csv.Append(FormatGridMoney(trade.Commission)).Append(',');
            csv.Append(FormatGridMoney(0)).Append(',');
            csv.Append(FormatGridMoney(0)).Append(',');
            csv.Append(FormatGridMoney(0)).Append(',');
            csv.Append(FormatGridMoney(0)).Append(',');
            csv.Append(FormatGridMoney(maeCurrency)).Append(',');
            csv.Append(FormatGridMoney(mfeCurrency)).Append(',');
            csv.Append(FormatGridMoney(etdCurrency)).Append(',');
            csv.Append("1").Append(',');
            csv.AppendLine();
        }

        private string Fmt(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private double TickValue()
        {
            return Instrument != null
                ? Instrument.MasterInstrument.PointValue * TickSize
                : 0;
        }

        private string FormatGridMoney(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture).Replace(".", ",") + " $";
        }

        private string FormatGridPrice(double price)
        {
            int decimals = GetPriceDecimals();
            return price.ToString("F" + decimals, CultureInfo.InvariantCulture).Replace(".", string.Empty);
        }

        private string FormatGridDateTime(DateTime value)
        {
            return value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private int GetPriceDecimals()
        {
            double tickSize = TickSize;

            if (tickSize <= 0)
                return 2;

            for (int decimals = 0; decimals <= 8; decimals++)
            {
                double scaled = tickSize * Math.Pow(10, decimals);
                if (Math.Abs(scaled - Math.Round(scaled)) < 0.0000001)
                    return decimals;
            }

            return 2;
        }

        private string SanitizeFileName(string value)
        {
            string result = value;

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                result = result.Replace(invalidChar, '_');

            return result;
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string folder = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int suffix = 1;

            string candidate;
            do
            {
                candidate = Path.Combine(folder, $"{fileName}_{suffix}{extension}");
                suffix++;
            }
            while (File.Exists(candidate));

            return candidate;
        }

        private string GridText(string value)
        {
            if (value == null)
                return string.Empty;

            return value.Replace(",", " ");
        }

        private string Csv(string value)
        {
            if (value == null)
                value = string.Empty;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private int HHmmToIntTime(int hhmm)
        {
            // Convierte 1530 -> 153000
            int hh = hhmm / 100;
            int mm = hhmm % 100;

            hh = Math.Max(0, Math.Min(23, hh));
            mm = Math.Max(0, Math.Min(59, mm));

            return (hh * 10000) + (mm * 100);
        }

        private int PriceToTicks(double priceDistance)
        {
            return (int)Math.Round(priceDistance / TickSize, MidpointRounding.AwayFromZero);
        }
    }
}
