#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Regresa : Strategy
    {
        public enum ProtectiveOrdersMode
        {
            PriorCloseWithStopMultiple,
            FixedTicks
        }

        private const int MaxLevels = 3;

        private static readonly string[] LongSignalNames = { "RegresaLongL1", "RegresaLongL2", "RegresaLongL3" };
        private static readonly string[] ShortSignalNames = { "RegresaShortL1", "RegresaShortL2", "RegresaShortL3" };

        private PriorDayOHLC priorDay;
        private bool[] levelUsedToday;
        private double[] levelReferenceClose;
        private bool sessionInitialized;
        private int tradesToday;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Estrategia de reversion a la media basada en el porcentaje de alejamiento respecto al cierre del dia anterior.";
                Name = "Regresa";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = MaxLevels;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 2;
                DefaultQuantity = 1;
                TraceOrders = false;

                EntryQuantity = 1;
                MaxTradesPerDay = 3;

                EnableLongs = true;
                EnableShorts = true;

                Level1Percent = 0.5;
                UseLevel2 = false;
                Level2Percent = 1.0;
                UseLevel3 = false;
                Level3Percent = 1.5;

                UseTradingWindow = false;
                StartTime = 93000;
                EndTime = 160000;

                ProtectiveOrders = ProtectiveOrdersMode.PriorCloseWithStopMultiple;
                StopLossTpMultiplier = 1.0;
                ManualTakeProfitTicks = 20;
                ManualStopLossTicks = 20;
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();
            }
            else if (State == State.DataLoaded)
            {
                priorDay = PriorDayOHLC();
                levelUsedToday = new bool[MaxLevels];
                levelReferenceClose = new double[MaxLevels];
                ResetLevelReferenceClose();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            ResetDailyStateIfNeeded();

            double priorClose = GetPriorClose();
            if (!IsValidPriceLevel(priorClose))
                return;

            if (MaxTradesPerDay > 0 && tradesToday >= MaxTradesPerDay)
                return;

            if (UseTradingWindow && !IsWithinTradingWindow(ToTime(Time[0])))
                return;

            TryEnterNextLevel(priorClose);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            int levelIndex;
            MarketPosition entryDirection;
            if (!TryGetEntrySignalMetadata(execution.Order.Name, out levelIndex, out entryDirection))
                return;

            double referenceClose = levelReferenceClose[levelIndex];
            if (!IsValidPriceLevel(referenceClose))
                return;

            SubmitProtectiveOrders(
                entryDirection,
                execution.Order.Name,
                execution.Order.Filled,
                execution.Order.AverageFillPrice,
                referenceClose);
        }

        private void TryEnterNextLevel(double priorClose)
        {
            int nextLevelIndex = GetNextEligibleLevelIndex();
            if (nextLevelIndex < 0)
                return;

            if (EnableLongs
                && (Position.MarketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Long)
                && Close[0] <= GetLongTriggerPrice(priorClose, nextLevelIndex))
            {
                SubmitEntry(MarketPosition.Long, nextLevelIndex, priorClose);
                return;
            }

            if (EnableShorts
                && (Position.MarketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Short)
                && Close[0] >= GetShortTriggerPrice(priorClose, nextLevelIndex))
            {
                SubmitEntry(MarketPosition.Short, nextLevelIndex, priorClose);
            }
        }

        private void SubmitEntry(MarketPosition direction, int levelIndex, double priorClose)
        {
            string signalName = direction == MarketPosition.Long
                ? LongSignalNames[levelIndex]
                : ShortSignalNames[levelIndex];

            double triggerPrice = direction == MarketPosition.Long
                ? GetLongTriggerPrice(priorClose, levelIndex)
                : GetShortTriggerPrice(priorClose, levelIndex);

            levelUsedToday[levelIndex] = true;
            levelReferenceClose[levelIndex] = priorClose;
            tradesToday++;

            if (direction == MarketPosition.Long)
                EnterLong(EntryQuantity, signalName);
            else
                EnterShort(EntryQuantity, signalName);

            Print(string.Format(
                "{0} | {1} nivel {2} enviado. Close={3} PriorClose={4} Trigger={5}",
                Time[0],
                direction == MarketPosition.Long ? "LONG" : "SHORT",
                levelIndex + 1,
                Close[0],
                priorClose,
                triggerPrice));
        }

        private void SubmitProtectiveOrders(
            MarketPosition direction,
            string entrySignalName,
            int quantity,
            double averageFillPrice,
            double priorClose)
        {
            double targetPrice;
            double stopPrice;

            if (ProtectiveOrders == ProtectiveOrdersMode.PriorCloseWithStopMultiple)
            {
                targetPrice = GetPriorCloseTargetPrice(direction, averageFillPrice, priorClose);

                int targetTicks = Math.Max(1, (int) Math.Round(Math.Abs(targetPrice - averageFillPrice) / TickSize));
                int stopTicks = Math.Max(1, (int) Math.Round(targetTicks * StopLossTpMultiplier));

                stopPrice = direction == MarketPosition.Long
                    ? Instrument.MasterInstrument.RoundToTickSize(averageFillPrice - (stopTicks * TickSize))
                    : Instrument.MasterInstrument.RoundToTickSize(averageFillPrice + (stopTicks * TickSize));
            }
            else
            {
                targetPrice = direction == MarketPosition.Long
                    ? Instrument.MasterInstrument.RoundToTickSize(averageFillPrice + (ManualTakeProfitTicks * TickSize))
                    : Instrument.MasterInstrument.RoundToTickSize(averageFillPrice - (ManualTakeProfitTicks * TickSize));

                stopPrice = direction == MarketPosition.Long
                    ? Instrument.MasterInstrument.RoundToTickSize(averageFillPrice - (ManualStopLossTicks * TickSize))
                    : Instrument.MasterInstrument.RoundToTickSize(averageFillPrice + (ManualStopLossTicks * TickSize));
            }

            if (direction == MarketPosition.Long)
            {
                ExitLongLimit(0, true, quantity, targetPrice, "TP_" + entrySignalName, entrySignalName);
                ExitLongStopMarket(0, true, quantity, stopPrice, "SL_" + entrySignalName, entrySignalName);
                return;
            }

            ExitShortLimit(0, true, quantity, targetPrice, "TP_" + entrySignalName, entrySignalName);
            ExitShortStopMarket(0, true, quantity, stopPrice, "SL_" + entrySignalName, entrySignalName);
        }

        private void ResetDailyStateIfNeeded()
        {
            if (sessionInitialized && !Bars.IsFirstBarOfSession)
                return;

            Array.Clear(levelUsedToday, 0, levelUsedToday.Length);
            ResetLevelReferenceClose();
            tradesToday = 0;
            sessionInitialized = true;
        }

        private void ResetLevelReferenceClose()
        {
            if (levelReferenceClose == null)
                return;

            for (int i = 0; i < levelReferenceClose.Length; i++)
                levelReferenceClose[i] = double.NaN;
        }

        private int GetNextEligibleLevelIndex()
        {
            // Los niveles se consumen en orden para escalar la entrada de forma progresiva.
            for (int i = 0; i < MaxLevels; i++)
            {
                if (!IsLevelEnabled(i))
                    continue;

                if (!levelUsedToday[i])
                    return i;
            }

            return -1;
        }

        private bool IsLevelEnabled(int levelIndex)
        {
            switch (levelIndex)
            {
                case 0:
                    return true;
                case 1:
                    return UseLevel2;
                case 2:
                    return UseLevel3;
                default:
                    return false;
            }
        }

        private double GetLevelPercent(int levelIndex)
        {
            switch (levelIndex)
            {
                case 0:
                    return Level1Percent;
                case 1:
                    return Level2Percent;
                case 2:
                    return Level3Percent;
                default:
                    throw new ArgumentOutOfRangeException("levelIndex", "Nivel no valido.");
            }
        }

        private double GetLongTriggerPrice(double priorClose, int levelIndex)
        {
            return priorClose * (1.0 - (GetLevelPercent(levelIndex) / 100.0));
        }

        private double GetShortTriggerPrice(double priorClose, int levelIndex)
        {
            return priorClose * (1.0 + (GetLevelPercent(levelIndex) / 100.0));
        }

        private double GetPriorClose()
        {
            return priorDay != null ? priorDay.PriorClose[0] : double.NaN;
        }

        private double GetPriorCloseTargetPrice(MarketPosition direction, double averageFillPrice, double priorClose)
        {
            double roundedPriorClose = Instrument.MasterInstrument.RoundToTickSize(priorClose);

            if (direction == MarketPosition.Long && roundedPriorClose <= averageFillPrice)
                return Instrument.MasterInstrument.RoundToTickSize(averageFillPrice + TickSize);

            if (direction == MarketPosition.Short && roundedPriorClose >= averageFillPrice)
                return Instrument.MasterInstrument.RoundToTickSize(averageFillPrice - TickSize);

            return roundedPriorClose;
        }

        private bool TryGetEntrySignalMetadata(string signalName, out int levelIndex, out MarketPosition direction)
        {
            for (int i = 0; i < MaxLevels; i++)
            {
                if (signalName == LongSignalNames[i])
                {
                    levelIndex = i;
                    direction = MarketPosition.Long;
                    return true;
                }

                if (signalName == ShortSignalNames[i])
                {
                    levelIndex = i;
                    direction = MarketPosition.Short;
                    return true;
                }
            }

            levelIndex = -1;
            direction = MarketPosition.Flat;
            return false;
        }

        private void ValidateConfiguration()
        {
            if (EntryQuantity <= 0)
                throw new ArgumentOutOfRangeException("EntryQuantity", "EntryQuantity debe ser mayor que 0.");

            if (MaxTradesPerDay <= 0 || MaxTradesPerDay > MaxLevels)
                throw new ArgumentOutOfRangeException("MaxTradesPerDay", "MaxTradesPerDay debe estar entre 1 y 3.");

            if (!EnableLongs && !EnableShorts)
                throw new ArgumentException("Debes habilitar compras, ventas o ambas.");

            if (Level1Percent <= 0)
                throw new ArgumentOutOfRangeException("Level1Percent", "Level1Percent debe ser mayor que 0.");

            if (UseLevel2 && Level2Percent <= Level1Percent)
                throw new ArgumentException("Level2Percent debe ser mayor que Level1Percent.");

            if (UseLevel3 && !UseLevel2)
                throw new ArgumentException("No puedes activar el nivel 3 si el nivel 2 esta desactivado.");

            if (UseLevel3 && Level3Percent <= Level2Percent)
                throw new ArgumentException("Level3Percent debe ser mayor que Level2Percent.");

            if (StartTime < 0 || StartTime > 235959)
                throw new ArgumentOutOfRangeException("StartTime", "StartTime debe estar entre 000000 y 235959.");

            if (EndTime < 0 || EndTime > 235959)
                throw new ArgumentOutOfRangeException("EndTime", "EndTime debe estar entre 000000 y 235959.");

            if (ProtectiveOrders == ProtectiveOrdersMode.PriorCloseWithStopMultiple && StopLossTpMultiplier <= 0)
                throw new ArgumentOutOfRangeException("StopLossTpMultiplier", "StopLossTpMultiplier debe ser mayor que 0.");

            if (ProtectiveOrders == ProtectiveOrdersMode.FixedTicks && ManualTakeProfitTicks <= 0)
                throw new ArgumentOutOfRangeException("ManualTakeProfitTicks", "ManualTakeProfitTicks debe ser mayor que 0.");

            if (ProtectiveOrders == ProtectiveOrdersMode.FixedTicks && ManualStopLossTicks <= 0)
                throw new ArgumentOutOfRangeException("ManualStopLossTicks", "ManualStopLossTicks debe ser mayor que 0.");
        }

        private bool IsWithinTradingWindow(int currentTime)
        {
            if (StartTime == EndTime)
                return true;

            if (StartTime < EndTime)
                return currentTime >= StartTime && currentTime <= EndTime;

            return currentTime >= StartTime || currentTime <= EndTime;
        }

        private bool IsValidPriceLevel(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos por entrada", GroupName = "01. Operativa", Order = 0)]
        public int EntryQuantity
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "Max operaciones por dia", GroupName = "01. Operativa", Order = 1)]
        public int MaxTradesPerDay
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar compras", GroupName = "01. Operativa", Order = 2)]
        public bool EnableLongs
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar ventas", GroupName = "01. Operativa", Order = 3)]
        public bool EnableShorts
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0001, 1000.0)]
        [Display(Name = "Nivel 1 (%)", GroupName = "02. Niveles", Order = 0)]
        public double Level1Percent
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar nivel 2", GroupName = "02. Niveles", Order = 1)]
        public bool UseLevel2
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0001, 1000.0)]
        [Display(Name = "Nivel 2 (%)", GroupName = "02. Niveles", Order = 2)]
        public double Level2Percent
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar nivel 3", GroupName = "02. Niveles", Order = 3)]
        public bool UseLevel3
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0001, 1000.0)]
        [Display(Name = "Nivel 3 (%)", GroupName = "02. Niveles", Order = 4)]
        public double Level3Percent
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar franja horaria", GroupName = "03. Horario", Order = 0)]
        public bool UseTradingWindow
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora inicio (HHmmss)", GroupName = "03. Horario", Order = 1)]
        public int StartTime
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Hora fin (HHmmss)", GroupName = "03. Horario", Order = 2)]
        public int EndTime
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo SL/TP", GroupName = "04. Riesgo", Order = 0)]
        public ProtectiveOrdersMode ProtectiveOrders
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 1000.0)]
        [Display(Name = "SL multiplo del TP", GroupName = "04. Riesgo", Order = 1)]
        public double StopLossTpMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP manual (ticks)", GroupName = "04. Riesgo", Order = 2)]
        public int ManualTakeProfitTicks
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SL manual (ticks)", GroupName = "04. Riesgo", Order = 3)]
        public int ManualStopLossTicks
        { get; set; }
    }
}
