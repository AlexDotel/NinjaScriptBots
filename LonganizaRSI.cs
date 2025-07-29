#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class LonganizaRSI : Strategy
	{

		// Parámetros personalizables
		private RSI rsi;
		private ADX adx;



		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Comprando con el rsi cruzando cierto nivel, y el ADX en crecimiento.";
				Name = "LonganizaRSI";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration = true;
			}
			else if (State == State.Configure)
			{
				AddDataSeries("ES 09-25", Data.BarsPeriodType.Tick, 1, Data.MarketDataType.Last);

				//Gestion de riesgo.
				SetStopLoss(@"Longaniza", CalculationMode.Ticks, StopLossTicks, false);
				SetProfitTarget(@"Longaniza", CalculationMode.Ticks, TakeProfitTicks);
				SetTrailStop(@"Longaniza", CalculationMode.Ticks, TrailingStopTicks, false);
			}
			else if (State == State.DataLoaded)
			{
				//Declarando Indicadores
				rsi = RSI(RsiPeriod, 3); // Puedes parametrizar también estos valores si lo deseas
				adx = ADX(AdxPeriod);
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{

			CheckFloatingLoss();
			CheckFloatingProfit();

		}

		protected override void OnBarUpdate()
		{


			//Comprobar barras suficientes
			if (BarsInProgress != 0 || CurrentBar < Math.Max(RsiLookbackPeriod + 2, AdxPeriod + 1))
				return;

			if (!EstaDentroDelHorario())
				return;

			//Logica
			int minBarsRequired = Math.Max(RsiLookbackPeriod + 2, AdxPeriod + 1);
			if (BarsInProgress != 0 || CurrentBar < minBarsRequired || rsi == null || adx == null)
				return;

			// Mostrar valores de RSI y ADX actuales
			Print(Time[0] + " | RSI[0]: " + rsi[0] + ", RSI[1]: " + rsi[1] + ", ADX[0]: " + adx[0]);

			// Confirmación RSI creciente
			bool rsiCreciente = true;
			for (int i = 1; i <= RsiLookbackPeriod; i++)
			{
				if (rsi[i] <= rsi[i + 1])
				{
					rsiCreciente = false;
					break;
				}
			}

			Print("RSI creciente en últimas " + RsiLookbackPeriod + " velas: " + rsiCreciente);

			// Verifica cruce RSI y confirmación ADX
			bool rsiCruzaNivel = rsi[1] <= RsiThreshold && rsi[0] > RsiThreshold;
			bool adxOk = adx[0] > AdxThreshold;

			Print("RSI cruza nivel: " + rsiCruzaNivel + ", ADX > umbral: " + adxOk);

			if (rsiCruzaNivel && rsiCreciente && adxOk)
			{
				if (Position.MarketPosition == MarketPosition.Flat)
				{
					Draw.ArrowUp(this, "RSIBreakoutEntry" + CurrentBar, false, 0, Low[0] - 2 * TickSize, Brushes.Green);
					Print(">>> Entrada larga ejecutada en: " + Time[0]);
					EnterLong(DefaultQuantity, "Longaniza");
				}
			}
		}

		void CheckFloatingLoss()
		{
			// Verifica si hay una posición abierta y si se debe aplicar el stop flotante 
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

				// Si trabajas en ticks
				double ticksLoss = Position.GetUnrealizedProfitLoss(PerformanceUnit.Points, Close[0]) / TickSize;

				if (!UsarDolaresEnVezDeTicks && ticksLoss <= -MaxFloatingLossTicks)
				{
					ExitLong("StopFlotanteTicks", "Longaniza");  // o ExitShort si es venta
				}
				else if (UsarDolaresEnVezDeTicks && unrealizedPnL <= -MaxFloatingLossCurrency)
				{
					ExitLong("StopFlotanteDolares", "Longaniza");
				}
			}
		}

		private void CheckFloatingProfit()
		{
			// Verifica si hay una posición abierta y si se debe aplicar el stop flotante 
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

				// Si trabajas en ticks
				double ticksProfit = Position.GetUnrealizedProfitLoss(PerformanceUnit.Points, Close[0]) / TickSize;

				if (!UsarDolaresEnVezDeTicks && ticksProfit >= MaxFloatingProfitTicks)
				{
					ExitLong("TakeProfitTicks", "Longaniza");  // o ExitShort si es venta
				}
				else if (UsarDolaresEnVezDeTicks && unrealizedPnL >= MaxFloatingProfitCurrency)
				{
					ExitLong("TakeProfitDolares", "Longaniza");
				}
			}
		}

		private bool EstaDentroDelHorario()
		{
			// Convertimos la hora actual de la barra a minutos desde la medianoche
			int minutosActuales = Times[0][0].Hour * 60 + Times[0][0].Minute;

			// Convertimos los inputs también a minutos desde medianoche
			int inicio = HoraInicio * 60 + MinutoInicio;
			int fin = HoraFin * 60 + MinutoFin;

			// Consideramos casos donde el horario puede cruzar la medianoche
			if (inicio <= fin)
				return minutosActuales >= inicio && minutosActuales <= fin;
			else
				return minutosActuales >= inicio || minutosActuales <= fin;
		}




		#region Propiedades

		[NinjaScriptProperty]
		[Display(GroupName = "RSI", Order = 1)]
		[Range(1, 100)]
		public int RsiPeriod { get; set; } = 14;

		[NinjaScriptProperty]
		[Display(GroupName = "RSI", Order = 2)]
		[Range(1, 100)]
		public double RsiThreshold { get; set; } = 50;

		[NinjaScriptProperty]
		[Display(GroupName = "RSI", Order = 3)]
		[Range(1, 50)]
		public int RsiLookbackPeriod { get; set; } = 2;

		[NinjaScriptProperty]
		public double StopLossTicks { get; set; } = 10;

		[NinjaScriptProperty]
		public double TakeProfitTicks { get; set; } = 10;

		[NinjaScriptProperty]
		public double TrailingStopTicks { get; set; } = 10;

		[NinjaScriptProperty]
		[Display(GroupName = "ADX", Order = 1)]
		[Range(1, 50)]
		public int AdxPeriod { get; set; } = 14;

		[NinjaScriptProperty]
		[Display(GroupName = "ADX", Order = 2)]
		[Range(5, 50)]
		public double AdxThreshold { get; set; } = 25;

		[NinjaScriptProperty]
		[Display(Name = "Dolares por Ticks?", GroupName = "Gestión de riesgo", Order = 1)]
		public bool UsarDolaresEnVezDeTicks { get; set; }  // true = dólares, false = ticks

		[NinjaScriptProperty]
		[Display(Name = "Máx pérdida flotante (ticks)", GroupName = "Gestión de riesgo", Order = 2)]
		public int MaxFloatingLossTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Máx pérdida flotante ($)", GroupName = "Gestión de riesgo", Order = 3)]
		public double MaxFloatingLossCurrency { get; set; }

		//Creamos los parametros para la funcion CheckFloatingProfit
		[NinjaScriptProperty]
		[Display(Name = "Máx ganancia flotante (ticks)", GroupName = "Gestión de riesgo", Order = 4)]
		public int MaxFloatingProfitTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Máx ganancia flotante ($)", GroupName = "Gestión de riesgo", Order = 5)]
		public double MaxFloatingProfitCurrency { get; set; }

		//HORARIO FILTRO
		[NinjaScriptProperty]
		[Range(0, 23), Display(Name = "Hora Inicio", GroupName = "Filtro Horario", Order = 1)]
		public int HoraInicio { get; set; } = 0;

		[NinjaScriptProperty]
		[Range(0, 59), Display(Name = "Minuto Inicio", GroupName = "Filtro Horario", Order = 2)]
		public int MinutoInicio { get; set; } = 0;

		[NinjaScriptProperty]
		[Range(0, 23), Display(Name = "Hora Fin", GroupName = "Filtro Horario", Order = 3)]
		public int HoraFin { get; set; } = 23;

		[NinjaScriptProperty]
		[Range(0, 59), Display(Name = "Minuto Fin", GroupName = "Filtro Horario", Order = 4)]
		public int MinutoFin { get; set; } = 59;



		#endregion

	}
}
