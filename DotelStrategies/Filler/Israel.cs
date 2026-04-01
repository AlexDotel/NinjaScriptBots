#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class Israel : Strategy
    {
        private RSI rsi;

        #region Inputs

        [NinjaScriptProperty]
        [Display(Name = "Cantidad", GroupName = "01. Orden", Order = 0)]
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "RSI Period", GroupName = "02. RSI", Order = 1)]
        [Range(1, int.MaxValue)]
        public int RSIPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "RSI Smooth", GroupName = "02. RSI", Order = 2)]
        [Range(1, int.MaxValue)]
        public int RSISmooth { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Nivel RSI", Description = "Nivel de cruce (ej: 50)", GroupName = "02. RSI", Order = 3)]
        public double RSILvl { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss (ticks)", GroupName = "03. Riesgo", Order = 4)]
        [Range(1, int.MaxValue)]
        public int StopLossTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Take Profit (ticks)", GroupName = "03. Riesgo", Order = 5)]
        [Range(1, int.MaxValue)]
        public int TakeProfitTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Hora inicio (double)", GroupName = "04. Horario", Order = 6)]
        public double StartTimeDouble { get; set; } = 9.00;

        [NinjaScriptProperty]
        [Display(Name = "Hora fin (double)", GroupName = "04. Horario", Order = 7)]
        public double EndTimeDouble { get; set; } = 17.00;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Israel";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 20;
            }
            else if (State == State.Configure)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
            else if (State == State.DataLoaded)
            {
                rsi = RSI(RSIPeriod, RSISmooth);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            if (!IsWithinTradingWindow(Time[0]))
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (CrossAbove(rsi, RSILvl, 1))
                {
                    EnterLong(Quantity, "RSILong");
                }
            }
        }

        #region Time Helpers

        private bool IsWithinTradingWindow(DateTime barTime)
        {
            int currentTime = ToTime(barTime);
            int startTime = ConvertDoubleTimeToInt(StartTimeDouble);
            int endTime = ConvertDoubleTimeToInt(EndTimeDouble);

            if (startTime < endTime)
                return currentTime >= startTime && currentTime <= endTime;

            return currentTime >= startTime || currentTime <= endTime;
        }

        private int ConvertDoubleTimeToInt(double timeValue)
        {
            int hour = (int)Math.Floor(timeValue);
            double decimalPart = timeValue - hour;

            int minutes = 0;

            if (Math.Abs(decimalPart - 0.25) < 0.0001) minutes = 15;
            else if (Math.Abs(decimalPart - 0.50) < 0.0001) minutes = 30;
            else if (Math.Abs(decimalPart - 0.75) < 0.0001) minutes = 45;

            return hour * 10000 + minutes * 100;
        }

        #endregion
    }
}