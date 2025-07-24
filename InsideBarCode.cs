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

namespace NinjaTrader.NinjaScript.Strategies
{
	public class InsideBarCode : Strategy
	{

		#region Variables
		double EntryPrice = 0;
		double StopPrice = 0;
		double TargetPrice = 0;
		double StopDistance = 0;

		private Swing Swing1;
		private double ultimoLow;
		private double ultimoHigh;
		
		private bool HighTomado = false;
		private bool LowTomado = false;
		#endregion


		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Inside bar bot";
				Name = "InsideBarCode";
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
				Fuerza = 10;
				Compras = true;
				Ventas = true;

			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				Swing1 = Swing(Close, Convert.ToInt32(Fuerza));
				Swing1.Plots[0].Brush = Brushes.Aqua;
				Swing1.Plots[1].Brush = Brushes.IndianRed;
				// AddChartIndicator(Swing1);
			}
		}


		#region OnBarUpdate
		protected override void OnBarUpdate()
		{

			if (BarsInProgress != 0)
				return;

			if (CurrentBars[0] < 2)
				return;

			// Verifica si es una nueva sesión
			if (Bars.IsFirstBarOfSession)
			{
				Draw.Dot(this, "NEW DAY" + CurrentBar, false, 0, Low[0] - TickSize * 10, Brushes.MediumSlateBlue);
			}

			//Obteniendo Highs and Lows en un rango de velas.
			int windowSize = Fuerza;

		    int centerHighIdx;
		    double swingHigh = GetSwingHigh(windowSize, out centerHighIdx);
		    if (!double.IsNaN(swingHigh))
		    {
		        Draw.Dot(this, "SwingHigh" + CurrentBar, false, centerHighIdx, swingHigh + TickSize * 5, Brushes.Red);
		    }
		
		    int centerLowIdx;
		    double swingLow = GetSwingLow(windowSize, out centerLowIdx);
		    if (!double.IsNaN(swingLow))
		    {
		        Draw.Dot(this, "SwingLow" + CurrentBar, false, centerLowIdx, swingLow - TickSize * 5, Brushes.Green);
		    }



			#region Toma High [VENTAS]
			if (!double.IsNaN(swingHigh) && High[1] >= swingHigh)
			{
				HighTomado = true;
				BackBrush = Brushes.IndianRed;
			}
			#endregion

			#region Toma Low [COMPRAS]
		    if (!double.IsNaN(swingLow) && Low[1] <= swingLow)
			{
				LowTomado = true;
				BackBrush = Brushes.CornflowerBlue;
			}
			#endregion

			#region Setup BAJISTA
			if (
				HighTomado
				 // Vela Anterior Alcista
				 && (Close[2] > Open[2])
				 // Vela Bajista
				 && (Close[1] < Open[1])
				 // Inside Bajista
				 && ((Open[1] <= Close[2])
				 && (Close[1] > Open[2])))
			{

				// Dibuja la flecha de senal
				Draw.ArrowDown(this, @"InsideBar Arrow down_1 " + Convert.ToString(CurrentBars[0]), false, 1, (High[1] + 2), Brushes.Red);

				// Dibujar lineas de Precios
				DrawPrices(false);

				// Resetear toma high
				HighTomado = false;

			}
			#endregion


			#region SetUp ALCISTA
			if (
				 LowTomado
				 // Vela Anterior Bajista
				 && (Close[2] < Open[2])
				 // Vela Alcista
				 && (Close[1] > Open[1])
				 // Inside Alcista
				 && ((Open[1] >= Close[2])
				 && (Close[1] < Open[2])))
			{

				// Dibuja la flecha de senal
				Draw.ArrowUp(this, @"InsideBar Arrow up_1 " + Convert.ToString(CurrentBars[0]), false, 1, (Low[1] - 2), Brushes.Lime);

				// Dibujar lineas de Precios
				DrawPrices(true);

				// Resetear toma low
				LowTomado = false;
			}
			#endregion

		}
		#endregion
		
		#region GetSwingPRICE
		private double GetSwingPrice(int windowSize, bool isHigh)
		{
		    int centerIndex = GetCenterIndex(windowSize);
		
		    // Verificar que haya suficientes barras
			if (CurrentBar < windowSize - 1)
				return double.NaN;

			
		    return isHigh ? High[centerIndex] : Low[centerIndex];
		}
		#endregion

		
		#region Primera Vela del Dia
		private bool IsNewSession()
		{
		    // Verifica si la fecha actual es diferente a la del último bar procesado
		    if (Bars.IsFirstBarOfSession)
		    {
				Draw.Dot(this, "NEW DAY" + CurrentBar, false, 0, Low[0] - TickSize * 10, Brushes.MediumSlateBlue);
		        return true;
		    }
		    return false;
		}
		#endregion

		
		#region ObtenerCentro (Indice del High)
		private int GetCenterIndex(int windowSize)
		{
		    if (windowSize < 3)
		        throw new ArgumentException("windowSize debe ser al menos 3.");
		
		    if (windowSize % 2 == 0)
		        windowSize++;  // Convierte a impar sumando 1
		
		    return windowSize / 2;
		}
		#endregion

		
		#region VerificarHigh
		private double GetSwingHigh(int windowSize, out int centerIndex)
		{
		    centerIndex = GetCenterIndex(windowSize);
		
		    if (CurrentBar < windowSize - 1)
		        return double.NaN;
		
		    double centerHigh = High[centerIndex];
		
		    for (int i = 0; i < windowSize; i++)
		    {
		        if (i == centerIndex)
		            continue;
		
		        if (High[i] >= centerHigh)
		            return double.NaN;
		    }
			
		    return centerHigh;
		}
		#endregion
		
		
		#region VerificarLow
		private double GetSwingLow(int windowSize, out int centerIndex)
		{
		    centerIndex = GetCenterIndex(windowSize);
		
		    if (CurrentBar < windowSize - 1)
		        return double.NaN;
		
		    double centerLow = Low[centerIndex];
		
		    for (int i = 0; i < windowSize; i++)
		    {
		        if (i == centerIndex)
		            continue;
		
		        if (Low[i] <= centerLow)
		            return double.NaN;
		    }
			
		    return centerLow;
		}
		#endregion
		
		
		#region DibujarPreciosOrden
		private void DrawPrices(bool alcista)
		{

			if (!alcista) //BAJISTA
			{
				//Calculamos las distancias del SL
				EntryPrice = Convert.ToDouble(Open[2]);
				StopPrice = Convert.ToDouble(High[2] + (5 * TickSize));
				StopDistance = Convert.ToDouble((Open[2] - StopPrice));
				TargetPrice = Convert.ToDouble(EntryPrice + StopDistance);

			}
			else if (alcista) //ALCISTA
			{
				//Calculamos las distancias del SL
				EntryPrice = Convert.ToDouble(Open[2]);
				StopPrice = Convert.ToDouble(Low[2] - (5 * TickSize));
				StopDistance = Convert.ToDouble((StopPrice - Open[2]));
				TargetPrice = Convert.ToDouble(EntryPrice - StopDistance);

			}


			// Dibuja la línea de entrada
			Draw.Line(
				this,
				@"LineaEntry" + Convert.ToString(CurrentBars[0]),
				false, // No es una línea persistente (se redibuja en cada render)
				2, EntryPrice, // Indice de la barra inicio , Precio
				0, EntryPrice, // Indice de la barra final , Precio
				Brushes.White, // Color de la línea
				DashStyleHelper.Solid, // Estilo de la línea
				1); // Grosor de la línea

			// Dibuja la línea de Stop
			Draw.Line(
				this,
				@"LineaStop" + Convert.ToString(CurrentBars[0]),
				false, // No es una línea persistente (se redibuja en cada render)
				2, StopPrice, // Indice de la barra inicio , Precio
				0, StopPrice, // Indice de la barra final , Precio
				Brushes.Red, // Color de la línea
				DashStyleHelper.Solid, // Estilo de la línea
				1); // Grosor de la línea

			// Dibuja la línea de Target
			Draw.Line(
				this,
				@"Lineatarget" + Convert.ToString(CurrentBars[0]),
				false, // No es una línea persistente (se redibuja en cada render)
				2, TargetPrice, // Indice de la barra inicio , Precio
				0, TargetPrice, // Indice de la barra final , Precio
				Brushes.Green, // Color de la línea
				DashStyleHelper.Solid, // Estilo de la línea
				1); // Grosor de la línea
		}
		#endregion

		
		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Fuerza", Order=1, GroupName="Parametros")]
		public int Fuerza
		{ get; set; }

		
		[NinjaScriptProperty]
		[Display(Name="Compras", Order=2, GroupName="Parametros")]
		public bool Compras
		{ get; set; }

		
		[NinjaScriptProperty]
		[Display(Name="Ventas", Order=2, GroupName="Parametros")]
		public bool Ventas
		{ get; set; }

		#endregion


	}
		
		
	
	
}
