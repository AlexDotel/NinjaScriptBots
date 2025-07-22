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
	public class PriorDayPruebaScript : Strategy
	{
		private double StopSize;
		private double TargetSize;
		private double StopPrice;
		private double TargetPrice;
		private bool CanTrade;
		private bool HighTomado;
		private bool LowTomado;

		private PriorDayOHLC PriorDayOHLC1;
		private PriorDayOHLC PriorDayOHLC2;
		

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"AlexDotel";
				Name										= "PriorDayPruebaScript";
				Calculate									= Calculate.OnEachTick;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
				Compras					= true;
				Ventas					= true;
				STime						= DateTime.Parse("15:30", System.Globalization.CultureInfo.InvariantCulture);
				ETime						= DateTime.Parse("18:30", System.Globalization.CultureInfo.InvariantCulture);
				StopSize					= 0;
				TargetSize					= 0;
				StopPrice					= 0;
				TargetPrice					= 0;
				CanTrade					= false;
				HighTomado					= false;
				LowTomado					= false;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(Data.BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{				
				PriorDayOHLC1				= PriorDayOHLC(Close);
				PriorDayOHLC2				= PriorDayOHLC(Close);
				PriorDayOHLC1.Plots[0].Brush = Brushes.SkyBlue;
				PriorDayOHLC1.Plots[1].Brush = Brushes.DodgerBlue;
				PriorDayOHLC1.Plots[2].Brush = Brushes.Crimson;
				PriorDayOHLC1.Plots[3].Brush = Brushes.LightCoral;
				AddChartIndicator(PriorDayOHLC1);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			CanTrade = false;
			if (CurrentBars[0] < 1
			|| CurrentBars[1] < 1)
				return;

			 // Filtro Horario
			if ((Times[0][0].TimeOfDay >= STime.TimeOfDay)
				 && (Times[0][0].TimeOfDay <= ETime.TimeOfDay))
			{
				CanTrade = true;
			}
			
			 // High Tomado
			if (CrossAbove(Close, PriorDayOHLC1.PriorHigh, 1))
			{
				HighTomado = true;
				LowTomado = false;
				Draw.Dot(this, @"PriorDayPrueba Dot_1 " + Convert.ToString(CurrentBars[0]), false, 0, PriorDayOHLC2.PriorHigh[0], Brushes.Red);
				
			}
			
			 // Low Tomado
			if (CrossBelow(Close, PriorDayOHLC1.PriorLow, 1))
			{
				LowTomado = true;
				HighTomado = false;
				Draw.Dot(this, @"PriorDayPrueba Dot_1 " + Convert.ToString(CurrentBars[0]), false, 0, PriorDayOHLC2.PriorLow[0], Brushes.Blue);
			}
			
			 // Setup Venta 
			if ((HighTomado == true)
				 && (CrossBelow(Close, PriorDayOHLC2.PriorHigh, 1))
				 && (CanTrade == true)
				 && Ventas == true)
			{
				EnterShort(Convert.ToInt32(DefaultQuantity), @"Venta");
				TargetPrice = PriorDayOHLC2.PriorLow[0];
				Draw.Text(this, @"TargetPriceText " + Convert.ToString(CurrentBars[0]), @"TargetPrice " + Convert.ToString(TargetPrice), 0, PriorDayOHLC2.PriorLow[0]);
				
				TargetSize = Close[1] - TargetPrice;
				Draw.Text(this, @"TargetSizeText " + Convert.ToString(CurrentBars[0]), @"TargetSize " + Convert.ToString(TargetSize), 0, High[0]);
				
				Print(TargetPrice);
				Print(TargetSize);
			}
			
			 // Setup Compra
			if ((LowTomado == true)
				 && (CrossAbove(Closes[1], PriorDayOHLC2.PriorLow, 1))
				 && (CanTrade == true)
				 && Compras == true)
			{
				//Compramos
				EnterLong(Convert.ToInt32(DefaultQuantity), @"Compra");
				Draw.Text(this, @"EntryPriceText " + Convert.ToString(CurrentBars[0]), @"EntryPrice " + Convert.ToString( Closes[1][0]), 0, Low[0] - 2);
				
				//Calculamos el Target Price segun el PriorHigh, que es a donde apuntaremos.
				TargetPrice = PriorDayOHLC2.PriorHigh[0];
				Draw.Text(this, @"TargetPriceText " + Convert.ToString(CurrentBars[0]), @"TargetPrice " + Convert.ToString(TargetPrice), 0, PriorDayOHLC2.PriorHigh[0] + 1);
				
				//Calculamos el tamaño del target en PUNTOS.
				TargetSize = TargetPrice - Closes[1][0];
				Draw.Text(this, @"TargetSizeText " + Convert.ToString(CurrentBars[0]), @"TargetSize " + Convert.ToString(TargetSize), 0,  PriorDayOHLC2.PriorHigh[0] + 0.5);
				
				//Calculamos el tamaño del Stop que sera en este caso la mitad del tamaño del Target.
				StopSize = ( TargetSize / 2 ) - 2;
				StopPrice = Closes[1][0] - StopSize;
				Draw.Text(this, @"StopPriceText " + Convert.ToString(CurrentBars[0]), @"StopPrice " + Convert.ToString(StopPrice), 0, Low[0] - 2.5);
				
				//Imprimimos en el OutPut
				Print(TargetPrice);
				Print(TargetSize);
				Print(StopPrice);
				Print(StopSize);
			}
			
			 // Set 7
			if ((Position.MarketPosition == MarketPosition.Short)
				 && (Closes[1][0] <= TargetSize))
			{
				ExitShort(Convert.ToInt32(DefaultQuantity), @"TargetVenta", @"Venta");
			}
			
			 // Set 8
			if ((Position.MarketPosition == MarketPosition.Long)
				 && (Closes[1][0] >= TargetSize))
			{
				ExitLong(Convert.ToInt32(DefaultQuantity), @"TargetCompra", @"Compra");
			}
			
		}
		
		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Compras", Order=1, GroupName="Parameters")]
		public bool Compras
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Ventas", Order=2, GroupName="Parameters")]
		public bool Ventas
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="STime", Order=3, GroupName="Parameters")]
		public DateTime STime
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="ETime", Order=4, GroupName="Parameters")]
		public DateTime ETime
		{ get; set; }
		#endregion

	}
}
