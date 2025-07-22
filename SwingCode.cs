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
	public class SwingCode : Strategy
	{
		private int Variable01;

		private Swing Swing1;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Swing and Variables";
				Name										= "SwingCode";
				Calculate									= Calculate.OnBarClose;
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
				Fuerza					= 10;
				Decimal					= 1;
				MyTime						= DateTime.Parse("15:30", System.Globalization.CultureInfo.InvariantCulture);
				Booleano					= true;
				CadenaTexto					= string.Empty;
				Variable01					= 1;
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{				
				Swing1				= Swing(Close, Convert.ToInt32(Fuerza));
				Swing1.Plots[0].Brush = Brushes.Aqua;
				Swing1.Plots[1].Brush = Brushes.IndianRed;
				AddChartIndicator(Swing1);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			if (CurrentBars[0] < 2)
				return;

			 // Set 1
			if ((Close[1] > Swing1.SwingHigh[2])
				 && (Close[2] < Swing1.SwingHigh[2]))
			{
				BackBrush = Brushes.IndianRed;
			}
			
			 // Set 2
			if ((Close[1] < Swing1.SwingLow[2])
				 && (Close[2] > Swing1.SwingLow[2]))
			{
				BackBrush = Brushes.CornflowerBlue;
			}
			
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Fuerza", Order=1, GroupName="Parameters")]
		public int Fuerza
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name="Decimal", Order=2, GroupName="Parameters")]
		public double Decimal
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="MyTime", Order=3, GroupName="Parameters")]
		public DateTime MyTime
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Booleano", Order=4, GroupName="Parameters")]
		public bool Booleano
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="CadenaTexto", Order=5, GroupName="Parameters")]
		public string CadenaTexto
		{ get; set; }
		#endregion

	}
}
