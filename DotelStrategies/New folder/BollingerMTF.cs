#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public enum BollingerMTFDirection
    {
        Ambas,
        SoloCompras,
        SoloVentas
    }

    public class BollingerMTF : Strategy
    {
        private const string LongSignal = "BMTF_Long";
        private const string ShortSignal = "BMTF_Short";

        private Bollinger lowerBollinger;
        private Bollinger higherBollinger;
        private TimeZoneInfo easternTimeZone;
        private int higherTimeframeBias;
        private int lastEntryBar;

        [NinjaScriptProperty]
        [Range(2, 300)]
        [Display(Name = "Periodo", GroupName = "01. Bollinger", Order = 0)]
        public int BollingerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Desviaciones", GroupName = "01. Bollinger", Order = 1)]
        public double BollingerStdDev { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "Temporalidad mayor (minutos)", Description = "60 equivale a H1.", GroupName = "02. Temporalidades", Order = 0)]
        public int HigherTimeframeMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dirección", GroupName = "03. Dirección", Order = 0)]
        public BollingerMTFDirection TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos", GroupName = "04. Gestión", Order = 0)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Stop loss (ticks)", GroupName = "04. Gestión", Order = 1)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Take profit (ticks)", GroupName = "04. Gestión", Order = 2)]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar filtro horario EST", GroupName = "05. Horario EST", Order = 0)]
        public bool UseEstTimeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Inicio EST (HHmm)", GroupName = "05. Horario EST", Order = 1)]
        public int StartEstHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Fin EST (HHmm)", GroupName = "05. Horario EST", Order = 2)]
        public int EndEstHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Cooldown (velas)", GroupName = "06. Backtest", Order = 0)]
        public int CooldownBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Bollinger principal", GroupName = "07. Visual", Order = 0)]
        public bool ShowBollinger { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BollingerMTF";
                Description = "Confirma un cruce de Bollinger en una temporalidad mayor y entra tras un cruce equivalente en la temporalidad principal.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                BollingerPeriod = 20;
                BollingerStdDev = 2.0;
                HigherTimeframeMinutes = 60;
                TradeDirection = BollingerMTFDirection.Ambas;
                Quantity = 1;
                StopLossTicks = 40;
                TakeProfitTicks = 80;
                UseEstTimeFilter = true;
                StartEstHHmm = 930;
                EndEstHHmm = 1600;
                CooldownBars = 0;
                ShowBollinger = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, HigherTimeframeMinutes);
                BarsRequiredToTrade = BollingerPeriod + 2;

                SetStopLoss(LongSignal, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(LongSignal, CalculationMode.Ticks, TakeProfitTicks);
                SetStopLoss(ShortSignal, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(ShortSignal, CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                lowerBollinger = Bollinger(Closes[0], BollingerStdDev, BollingerPeriod);
                higherBollinger = Bollinger(Closes[1], BollingerStdDev, BollingerPeriod);
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                higherTimeframeBias = 0;
                lastEntryBar = -1;

                if (ShowBollinger)
                    AddChartIndicator(lowerBollinger);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < BollingerPeriod + 1 || CurrentBars[1] < BollingerPeriod + 1)
                return;

            if (BarsInProgress == 1)
            {
                UpdateHigherTimeframeBias();
                return;
            }

            if (BarsInProgress != 0 || Position.MarketPosition != MarketPosition.Flat)
                return;

            if (!IsInsideEstWindow(Time[0]))
                return;

            if (lastEntryBar >= 0 && CurrentBar - lastEntryBar <= CooldownBars)
                return;

            bool lowerCrossedAbove = Close[0] > lowerBollinger.Upper[0]
                && Close[1] <= lowerBollinger.Upper[1];
            bool lowerCrossedBelow = Close[0] < lowerBollinger.Lower[0]
                && Close[1] >= lowerBollinger.Lower[1];

            if (higherTimeframeBias == 1 && lowerCrossedAbove && AllowsLongs())
            {
                EnterLong(Quantity, LongSignal);
                lastEntryBar = CurrentBar;
            }
            else if (higherTimeframeBias == -1 && lowerCrossedBelow && AllowsShorts())
            {
                EnterShort(Quantity, ShortSignal);
                lastEntryBar = CurrentBar;
            }
        }

        private void UpdateHigherTimeframeBias()
        {
            bool crossedAbove = Closes[1][0] > higherBollinger.Upper[0]
                && Closes[1][1] <= higherBollinger.Upper[1];
            bool crossedBelow = Closes[1][0] < higherBollinger.Lower[0]
                && Closes[1][1] >= higherBollinger.Lower[1];

            if (crossedAbove)
                higherTimeframeBias = 1;
            else if (crossedBelow)
                higherTimeframeBias = -1;
            else if (Closes[1][0] <= higherBollinger.Upper[0]
                && Closes[1][0] >= higherBollinger.Lower[0])
                higherTimeframeBias = 0;
        }

        private bool AllowsLongs()
        {
            return TradeDirection == BollingerMTFDirection.Ambas
                || TradeDirection == BollingerMTFDirection.SoloCompras;
        }

        private bool AllowsShorts()
        {
            return TradeDirection == BollingerMTFDirection.Ambas
                || TradeDirection == BollingerMTFDirection.SoloVentas;
        }

        private bool IsInsideEstWindow(DateTime barTime)
        {
            if (!UseEstTimeFilter)
                return true;

            DateTime localTime = DateTime.SpecifyKind(barTime, DateTimeKind.Local);
            DateTime easternTime = TimeZoneInfo.ConvertTime(localTime, easternTimeZone);
            int now = easternTime.Hour * 100 + easternTime.Minute;
            int start = NormalizeHHmm(StartEstHHmm);
            int end = NormalizeHHmm(EndEstHHmm);

            return start <= end
                ? now >= start && now <= end
                : now >= start || now <= end;
        }

        private int NormalizeHHmm(int value)
        {
            int hours = Math.Max(0, Math.Min(23, value / 100));
            int minutes = Math.Max(0, Math.Min(59, value % 100));
            return hours * 100 + minutes;
        }
    }
}
