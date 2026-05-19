#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ArgosFlux : Strategy
    {
        public enum TradeDirectionOption
        {
            Both,
            LongOnly,
            ShortOnly
        }

        private const string LongSignalName = "ArgosFluxLong";
        private const string ShortSignalName = "ArgosFluxShort";

        private DisparityIndex disparityIndex;

        private double sessionPriceVolume;
        private double sessionVolume;
        private double currentSessionVwap;
        private double previousBarClose;
        private double previousBarVwap;
        private bool hasPreviousBarSnapshot;
        private bool sessionInitialized;
        private DateTime lastSessionResetBarTime;

        [NinjaScriptProperty]
        [Display(Name = "Direccion permitida", GroupName = "01. General", Order = 0)]
        public TradeDirectionOption AllowedDirection
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Cantidad", GroupName = "01. General", Order = 1)]
        public int OrderQuantity
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DIX periodo", GroupName = "02. DIX", Order = 0)]
        public int DixPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "SL multiplo DIX", GroupName = "02. DIX", Order = 1)]
        public double StopLossDixMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "TP multiplo DIX", GroupName = "02. DIX", Order = 2)]
        public double TakeProfitDixMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar ventana 1", GroupName = "03. Ventana 1", Order = 0)]
        public bool UseSession1
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Inicio 1 (HHmmss)", GroupName = "03. Ventana 1", Order = 1)]
        public int Session1Start
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Fin 1 (HHmmss)", GroupName = "03. Ventana 1", Order = 2)]
        public int Session1End
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar ventana 2", GroupName = "04. Ventana 2", Order = 0)]
        public bool UseSession2
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Inicio 2 (HHmmss)", GroupName = "04. Ventana 2", Order = 1)]
        public int Session2Start
        { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Fin 2 (HHmmss)", GroupName = "04. Ventana 2", Order = 2)]
        public int Session2End
        { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ArgosFlux";
                Description = "Cruce de VWAP manual con bot simple OnBarClose. El stop loss y el take profit se calculan solo a partir de DIX (Disparity Index), con filtros de horario y direccion.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.WaitUntilFlat;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                DefaultQuantity = 1;
                BarsRequiredToTrade = 26;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = false;
                IncludeCommission = true;

                AllowedDirection = TradeDirectionOption.Both;
                OrderQuantity = 1;

                DixPeriod = 25;
                StopLossDixMultiplier = 1.0;
                TakeProfitDixMultiplier = 1.0;

                UseSession1 = true;
                Session1Start = 93000;
                Session1End = 120000;
                UseSession2 = true;
                Session2Start = 130000;
                Session2End = 160000;

                ResetVwapState();
            }
            else if (State == State.Configure)
            {
                ValidateConfiguration();
                BarsRequiredToTrade = Math.Max(2, DixPeriod + 1);
            }
            else if (State == State.DataLoaded)
            {
                disparityIndex = DisparityIndex(DixPeriod);
                ResetVwapState();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade - 1)
                return;

            if (!TryUpdateManualVwap())
                return;

            bool crossedAbove = false;
            bool crossedBelow = false;

            if (hasPreviousBarSnapshot)
            {
                crossedAbove = previousBarClose <= previousBarVwap && Close[0] > currentSessionVwap;
                crossedBelow = previousBarClose >= previousBarVwap && Close[0] < currentSessionVwap;
            }

            if (Position.MarketPosition == MarketPosition.Flat && IsWithinTradingWindow(ToTime(Time[0])))
            {
                if (crossedAbove && CanTradeLong())
                {
                    PrepareDixProtectiveOrders(LongSignalName);
                    EnterLong(OrderQuantity, LongSignalName);
                }
                else if (crossedBelow && CanTradeShort())
                {
                    PrepareDixProtectiveOrders(ShortSignalName);
                    EnterShort(OrderQuantity, ShortSignalName);
                }
            }

            previousBarClose = Close[0];
            previousBarVwap = currentSessionVwap;
            hasPreviousBarSnapshot = true;
        }

        private bool TryUpdateManualVwap()
        {
            ResetVwapSessionIfNeeded();

            double barVolume = Volume[0];
            if (barVolume <= 0)
                return false;

            double weightedPrice = (High[0] + Low[0] + Close[0]) / 3.0;
            sessionPriceVolume += weightedPrice * barVolume;
            sessionVolume += barVolume;

            if (sessionVolume <= 0)
                return false;

            currentSessionVwap = sessionPriceVolume / sessionVolume;
            return IsValidNumber(currentSessionVwap);
        }

        private void ResetVwapSessionIfNeeded()
        {
            if (!sessionInitialized)
            {
                sessionInitialized = true;
                lastSessionResetBarTime = Time[0];
                return;
            }

            if (!Bars.IsFirstBarOfSession)
                return;

            if (lastSessionResetBarTime == Time[0])
                return;

            sessionPriceVolume = 0.0;
            sessionVolume = 0.0;
            currentSessionVwap = double.NaN;
            previousBarClose = double.NaN;
            previousBarVwap = double.NaN;
            hasPreviousBarSnapshot = false;
            lastSessionResetBarTime = Time[0];
        }

        private void PrepareDixProtectiveOrders(string signalName)
        {
            int baseDixTicks = GetBaseDixTicks();
            int stopTicks = Math.Max(1, (int)Math.Round(baseDixTicks * StopLossDixMultiplier));
            int targetTicks = Math.Max(1, (int)Math.Round(baseDixTicks * TakeProfitDixMultiplier));

            SetStopLoss(signalName, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, targetTicks);
        }

        private int GetBaseDixTicks()
        {
            double dixValue = disparityIndex == null ? double.NaN : disparityIndex[0];
            if (!IsValidNumber(dixValue))
                return 1;

            // DIX es porcentual. Se convierte a distancia de precio para llevarla a ticks.
            double dixPriceDistance = Math.Abs(dixValue) * Close[0] / 100.0;
            int dixTicks = (int)Math.Round(dixPriceDistance / TickSize);

            return Math.Max(1, dixTicks);
        }

        private bool CanTradeLong()
        {
            return AllowedDirection == TradeDirectionOption.Both || AllowedDirection == TradeDirectionOption.LongOnly;
        }

        private bool CanTradeShort()
        {
            return AllowedDirection == TradeDirectionOption.Both || AllowedDirection == TradeDirectionOption.ShortOnly;
        }

        private bool IsWithinTradingWindow(int currentTime)
        {
            if (!UseSession1 && !UseSession2)
                return true;

            bool inSession1 = UseSession1 && IsWithinSession(currentTime, Session1Start, Session1End);
            bool inSession2 = UseSession2 && IsWithinSession(currentTime, Session2Start, Session2End);

            return inSession1 || inSession2;
        }

        private bool IsWithinSession(int currentTime, int sessionStart, int sessionEnd)
        {
            if (sessionStart == sessionEnd)
                return true;

            if (sessionStart < sessionEnd)
                return currentTime >= sessionStart && currentTime <= sessionEnd;

            return currentTime >= sessionStart || currentTime <= sessionEnd;
        }

        private void ResetVwapState()
        {
            sessionPriceVolume = 0.0;
            sessionVolume = 0.0;
            currentSessionVwap = double.NaN;
            previousBarClose = double.NaN;
            previousBarVwap = double.NaN;
            hasPreviousBarSnapshot = false;
            sessionInitialized = false;
            lastSessionResetBarTime = DateTime.MinValue;
        }

        private void ValidateConfiguration()
        {
            if (OrderQuantity <= 0)
                throw new ArgumentOutOfRangeException("OrderQuantity", "OrderQuantity debe ser mayor o igual que 1.");

            if (DixPeriod <= 0)
                throw new ArgumentOutOfRangeException("DixPeriod", "DixPeriod debe ser mayor que 0.");

            if (StopLossDixMultiplier <= 0)
                throw new ArgumentOutOfRangeException("StopLossDixMultiplier", "StopLossDixMultiplier debe ser mayor que 0.");

            if (TakeProfitDixMultiplier <= 0)
                throw new ArgumentOutOfRangeException("TakeProfitDixMultiplier", "TakeProfitDixMultiplier debe ser mayor que 0.");

            if (UseSession1)
            {
                ValidateTimeValue(Session1Start, "Session1Start");
                ValidateTimeValue(Session1End, "Session1End");
            }

            if (UseSession2)
            {
                ValidateTimeValue(Session2Start, "Session2Start");
                ValidateTimeValue(Session2End, "Session2End");
            }
        }

        private void ValidateTimeValue(int value, string parameterName)
        {
            if (value < 0 || value > 235959)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    parameterName + " debe estar en formato HHmmss entre 000000 y 235959.");
            }

            int hours = value / 10000;
            int minutes = (value / 100) % 100;
            int seconds = value % 100;

            if (hours > 23 || minutes > 59 || seconds > 59)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    parameterName + " no es una hora valida en formato HHmmss.");
            }
        }

        private bool IsValidNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
