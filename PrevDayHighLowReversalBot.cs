using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class PrevDayHighLowReversalBot : Strategy
    {
        public enum StopMode
        {
            ManualTicks = 0,
            AtrRisk = 1
        }

        public enum TradeDirectionMode
        {
            Both = 0,
            LongOnly = 1,
            ShortOnly = 2
        }

        #region Inputs
        [NinjaScriptProperty]
        [Display(Name = "Modo de stops", Order = 1, GroupName = "01. Stops")]
        public StopMode StopType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SL manual (ticks)", Order = 2, GroupName = "01. Stops")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP manual (ticks)", Order = 3, GroupName = "01. Stops")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ATR periodo", Order = 4, GroupName = "02. ATR / Riesgo")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "ATR multiplo (SL)", Order = 5, GroupName = "02. ATR / Riesgo")]
        public double AtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "TP multiplo del SL", Order = 6, GroupName = "02. ATR / Riesgo")]
        public double TakeProfitRR { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Riesgo $ por operacion", Order = 7, GroupName = "02. ATR / Riesgo")]
        public double RiskDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Max contratos", Order = 8, GroupName = "02. ATR / Riesgo")]
        public int MaxContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir 1 contrato si riesgo insuficiente", Order = 9, GroupName = "02. ATR / Riesgo")]
        public bool AllowMinContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Hora inicio (HHmm)", Order = 10, GroupName = "03. Horario")]
        public int StartTimeHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Hora fin (HHmm)", Order = 11, GroupName = "03. Horario")]
        public int EndTimeHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Horas excluidas (CSV)", Order = 12, GroupName = "03. Horario")]
        public string ExcludeHoursCsv { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Direccion operativa", Order = 20, GroupName = "04. Direccion")]
        public TradeDirectionMode Direction { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug", Order = 30, GroupName = "05. Debug")]
        public bool PrintDebug { get; set; }
        #endregion

        #region Private fields
        private PriorDayOHLC priorDay;
        private ATR atr;
        private double tickValue;

        private int startTimeHHmmss;
        private int endTimeHHmmss;
        private bool[] excludedHours;

        private bool invalidManualConfig;
        private bool printedInvalidConfig;
        private bool printedRiskTooLow;
        private bool tradedToday;
        private DateTime lastTradeDate = Core.Globals.MinDate;

        private const string LongSignal = "PDHL_LONG";
        private const string ShortSignal = "PDHL_SHORT";
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Reversal: vende si Close supera el High del dia anterior, compra si Close rompe el Low del dia anterior. SL/TP manual o SL ATR con riesgo y TP por multiplo.";
                Name = "PrevDayHighLowReversalBot";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                IsInstantiatedOnEachOptimizationIteration = false;
                BarsRequiredToTrade = 20;

                StopType = StopMode.ManualTicks;
                StopLossTicks = 20;
                ProfitTargetTicks = 20;

                AtrPeriod = 14;
                AtrMultiplier = 2.0;
                TakeProfitRR = 1.0;
                RiskDollars = 100.0;
                MaxContracts = 10;
                AllowMinContracts = true;

                StartTimeHHmm = 1530;
                EndTimeHHmm = 2100;
                ExcludeHoursCsv = "16,18,19";

                Direction = TradeDirectionMode.Both;
                PrintDebug = true;
            }
            else if (State == State.Configure)
            {
                startTimeHHmmss = HHmmToHHmmss(StartTimeHHmm);
                endTimeHHmmss = HHmmToHHmmss(EndTimeHHmm);

                excludedHours = ParseExcludedHours(ExcludeHoursCsv);

                invalidManualConfig = false;
                if (StopType == StopMode.ManualTicks && StopLossTicks < ProfitTargetTicks)
                    invalidManualConfig = true;

                int required = 1;
                if (StopType == StopMode.AtrRisk)
                    required = Math.Max(required, AtrPeriod + 1);
                BarsRequiredToTrade = Math.Max(BarsRequiredToTrade, required);

                if (PrintDebug)
                {
                    Print($"{Name} config: StopType={StopType}, SL={StopLossTicks}, TP={ProfitTargetTicks}, ATR={AtrPeriod}, ATRx={AtrMultiplier}, RR={TakeProfitRR}, Risk={RiskDollars}, MaxC={MaxContracts}");
                    Print($"Horario: {StartTimeHHmm}-{EndTimeHHmm}, Excluidas: {ExcludeHoursCsv}");
                }
            }
            else if (State == State.DataLoaded)
            {
                priorDay = PriorDayOHLC();

                if (StopType == StopMode.AtrRisk)
                    atr = ATR(AtrPeriod);

                if (Instrument != null)
                    tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (Bars.IsFirstBarOfSession)
            {
                DateTime d = Time[0].Date;
                if (d != lastTradeDate)
                {
                    lastTradeDate = d;
                    tradedToday = false;
                }
            }

            if (invalidManualConfig)
            {
                PrintInvalidConfigOnce();
                return;
            }

            if (CurrentBar < 1)
                return;

            if (!IsWithinTradingHours())
                return;

            if (IsExcludedHour(Time[0]))
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (tradedToday)
                return;

            double priorHigh = priorDay?.PriorHigh[0] ?? 0.0;
            double priorLow = priorDay?.PriorLow[0] ?? 0.0;
            if (priorHigh <= 0.0 || priorLow <= 0.0)
                return;

            bool shortSignal = Close[0] > priorHigh;
            bool longSignal = Close[0] < priorLow;

            if (!shortSignal && !longSignal)
                return;

            int stopTicks;
            int targetTicks;
            int qty;

            if (!TryGetStopsAndQty(out stopTicks, out targetTicks, out qty))
                return;

            if (longSignal && (Direction == TradeDirectionMode.Both || Direction == TradeDirectionMode.LongOnly))
                SubmitLong(stopTicks, targetTicks, qty, priorLow);
            else if (shortSignal && (Direction == TradeDirectionMode.Both || Direction == TradeDirectionMode.ShortOnly))
                SubmitShort(stopTicks, targetTicks, qty, priorHigh);
        }

        private bool TryGetStopsAndQty(out int stopTicks, out int targetTicks, out int qty)
        {
            stopTicks = 0;
            targetTicks = 0;
            qty = 0;

            if (StopType == StopMode.ManualTicks)
            {
                stopTicks = StopLossTicks;
                targetTicks = ProfitTargetTicks;
                return stopTicks > 0 && targetTicks > 0;
            }

            if (atr == null)
                atr = ATR(AtrPeriod);

            if (CurrentBar < AtrPeriod)
                return false;

            double atrValue = atr[0];
            if (atrValue <= 0.0)
                return false;

            double stopDistance = atrValue * AtrMultiplier;
            stopTicks = (int)Math.Ceiling(stopDistance / TickSize);
            if (stopTicks <= 0)
                return false;

            targetTicks = (int)Math.Round(stopTicks * TakeProfitRR, MidpointRounding.AwayFromZero);
            if (targetTicks <= 0)
                return false;

            qty = GetPositionSizeByRisk(stopTicks);
            if (qty <= 0)
            {
                PrintRiskTooLowOnce(stopTicks);
                return false;
            }

            return true;
        }

        private int GetPositionSizeByRisk(int stopTicks)
        {
            if (stopTicks <= 0)
                return 0;

            if (tickValue <= 0.0 && Instrument != null)
                tickValue = Instrument.MasterInstrument.PointValue * TickSize;

            double riskPerContract = stopTicks * tickValue;
            if (riskPerContract <= 0.0)
                return 0;

            int size = (int)Math.Floor(RiskDollars / riskPerContract);
            if (size < 1)
            {
                if (AllowMinContracts)
                {
                    PrintRiskTooLowOnce(stopTicks);
                    return Math.Min(1, MaxContracts);
                }

                return 0;
            }

            return Math.Min(size, MaxContracts);
        }

        private void SubmitLong(int stopTicks, int targetTicks, int qty, double priorLow)
        {
            SetStopLoss(LongSignal, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(LongSignal, CalculationMode.Ticks, targetTicks);

            if (PrintDebug)
                Print($"{Time[0]:yyyy-MM-dd HH:mm} LONG | Close={Close[0]:0.00} PriorLow={priorLow:0.00} SL={stopTicks} TP={targetTicks} Qty={(StopType == StopMode.AtrRisk ? qty.ToString() : "Default")}");

            if (StopType == StopMode.AtrRisk)
                EnterLong(qty, LongSignal);
            else
                EnterLong(LongSignal);

            tradedToday = true;
        }

        private void SubmitShort(int stopTicks, int targetTicks, int qty, double priorHigh)
        {
            SetStopLoss(ShortSignal, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(ShortSignal, CalculationMode.Ticks, targetTicks);

            if (PrintDebug)
                Print($"{Time[0]:yyyy-MM-dd HH:mm} SHORT | Close={Close[0]:0.00} PriorHigh={priorHigh:0.00} SL={stopTicks} TP={targetTicks} Qty={(StopType == StopMode.AtrRisk ? qty.ToString() : "Default")}");

            if (StopType == StopMode.AtrRisk)
                EnterShort(qty, ShortSignal);
            else
                EnterShort(ShortSignal);

            tradedToday = true;
        }

        private bool IsWithinTradingHours()
        {
            if (StartTimeHHmm == 0 && EndTimeHHmm == 0)
                return true;

            int now = ToTime(Time[0]);

            if (startTimeHHmmss <= endTimeHHmmss)
                return now >= startTimeHHmmss && now <= endTimeHHmmss;

            return now >= startTimeHHmmss || now <= endTimeHHmmss;
        }

        private bool IsExcludedHour(DateTime time)
        {
            if (excludedHours == null || excludedHours.Length != 24)
                return false;

            int hour = time.Hour;
            if (hour < 0 || hour > 23)
                return false;

            return excludedHours[hour];
        }

        private bool[] ParseExcludedHours(string csv)
        {
            bool[] result = new bool[24];
            if (string.IsNullOrWhiteSpace(csv))
                return result;

            string[] parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                int hour;
                if (!int.TryParse(part, out hour))
                    continue;

                if (hour >= 0 && hour <= 23)
                    result[hour] = true;
            }

            return result;
        }

        private int HHmmToHHmmss(int hhmm)
        {
            int hh = hhmm / 100;
            int mm = hhmm % 100;

            if (hh < 0 || hh > 23)
                hh = 0;
            if (mm < 0 || mm > 59)
                mm = 0;

            return (hh * 10000) + (mm * 100);
        }

        private void PrintInvalidConfigOnce()
        {
            if (printedInvalidConfig)
                return;

            printedInvalidConfig = true;
            Print($"{Name} detenido: SL ({StopLossTicks}) < TP ({ProfitTargetTicks}). Iteracion de optimizacion sin trading.");
        }

        private void PrintRiskTooLowOnce(int stopTicks)
        {
            if (printedRiskTooLow)
                return;

            printedRiskTooLow = true;
            string action = AllowMinContracts ? "Se fuerza 1 contrato." : "Sin trade.";
            Print($"{Name} riesgo $ insuficiente. SL={stopTicks} ticks, Risk$={RiskDollars}. {action}");
        }
    }
}
