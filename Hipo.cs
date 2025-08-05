#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
#endregion

// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class Hipo : Strategy
    {
        

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Fast = 10;
                Slow = 25;
                Calculate = Calculate.OnBarClose;
                Name = "Hipo";
            }

            else if (State == State.Configure)
            {

                AddDataSeries(Data.BarsPeriodType.Tick, 1);


                AddChartIndicator(EMA(Fast));
                AddChartIndicator(EMA(Slow));


                EMA(Fast).Plots[0].Brush = Brushes.Blue;
                EMA(Slow).Plots[0].Brush = Brushes.Green;

                // Set the stop loss and take profit levels
                SetStopLoss("Long", CalculationMode.Ticks, TicksForSL, false);
                SetProfitTarget("Long", CalculationMode.Ticks, TicksForTP);

                // Set the stop loss and take profit levels
                SetStopLoss("Short", CalculationMode.Ticks, TicksForSL, false);
                SetProfitTarget("Short", CalculationMode.Ticks, TicksForTP);

            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
               
                if (!IsWithinTradingHours()) return;

                if (Position.MarketPosition == MarketPosition.Flat && IsADXStrong() && IsRSIUptrend())
                {
                    if (CrossAbove(EMA(Fast), EMA(Slow), 1) && IsPriceAboveEMA200())
                    {
                        EnterLong(1, 1, "Long");
                    }
                    else if (CrossBelow(EMA(Fast), EMA(Slow), 1) && IsPriceBelowEMA200() && IsRSIDowntrend())
                    {
                        EnterShort(1, 1, "Short");
                    }
                }
            }
            else
            {
                return;
            }
        }

        //Funcion para verificar el horario
        private bool IsWithinTradingHours()
        {
            DateTime now = Times[0][0];
            DateTime startTime = now.Date.AddHours(HoraInicio).AddMinutes(MinutoInicio);
            DateTime endTime = now.Date.AddHours(HoraFin).AddMinutes(MinutoFin);
            return now >= startTime && now <= endTime;
        }

        //Funcion comprobando RSI alcista
        private bool IsRSIUptrend()
        {
            double rsiValue = RSI(14, 3)[0]; // Assuming a 14-period RSI with a smoothing of 3
            return rsiValue > 50; // Check if RSI is above 50 for an uptrend
        }

        //Funcion comprobando RSI bajista
        private bool IsRSIDowntrend()
        {
            double rsiValue = RSI(14, 3)[0]; // Assuming a 14-period RSI with a smoothing of 3
            return rsiValue < 50; // Check if RSI is below 50 for a downtrend
        }

        //Funcion para verificar si el precio esta por encima de la EMA 200
        private bool IsPriceAboveEMA200()
        {
            return Closes[0][0] > EMA(EMAPeriod)[0];
        }

        //Funcion para verificar si el precio esta por debajo de la EMA 200
        private bool IsPriceBelowEMA200()
        {
            return Closes[0][0] < EMA(EMAPeriod)[0];
        }
        
        //Funcion para verificar si el ADX es mayor a 20
        private bool IsADXStrong()
        {
            double adxValue = ADX(ADXPeriod)[0]; // Assuming a 14-period ADX
            return adxValue > ADXLevel;
        }

        #region Properties


        //Input para la hora de inicio
        [Range(0, 23), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Start Hour", GroupName = "NinjaScriptParameters", Order = 0)]
        public int HoraInicio
        { get; set; } = 15;

        //Input para el minuto de inicio
        [Range(0, 59), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Start Minute", GroupName = "NinjaScriptParameters", Order = 1)]
        public int MinutoInicio
        { get; set; } = 30;

        //Input para la hora de fin
        [Range(0, 23), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "End Hour", GroupName = "NinjaScriptParameters", Order = 2)]
        public int HoraFin  
        { get; set; } = 16;

        //Input para el minuto de fin
        [Range(0, 59), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "End Minute", GroupName = "NinjaScriptParameters", Order = 3)]
        public int MinutoFin
        { get; set; } = 00;

        //Input para el periodo de la EMA
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "EMA Period", GroupName = "NinjaScriptParameters", Order = 2)]
        public int EMAPeriod
        { get; set; } = 200;    

        //Input para el nivel mínimo del ADX
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "ADX Level", GroupName = "NinjaScriptParameters", Order = 3)]
        public int ADXLevel
        { get; set; } = 24;

        //Input para los periodos del ADX
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "ADX Period", GroupName = "NinjaScriptParameters", Order = 4)]
        public int ADXPeriod
        { get; set; } = 14;

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Fast", GroupName = "NinjaScriptParameters", Order = 0)]
        public int Fast
        { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Slow", GroupName = "NinjaScriptParameters", Order = 0)]
        public int Slow
        { get; set; }

        //Input ticks para el TP
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Ticks for TP", GroupName = "NinjaScriptParameters", Order = 1)]
        public int TicksForTP
        { get; set; } = 61;

        //Input ticks para el SL
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Ticks for SL", GroupName = "NinjaScriptParameters", Order = 2)]
        public int TicksForSL
        { get; set; } = 51;

		#endregion
    }
}
