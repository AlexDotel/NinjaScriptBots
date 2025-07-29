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
	public class ORBITECH : Strategy
	{
		
		
	    // === Internals ===
	    private double rangeHigh = double.MinValue;
	    private double rangeLow = double.MaxValue;
	    private bool rangeComplete = false;
	    private bool tradeTaken = false;
	    private DateTime entryTime;
	    private double entryPrice;

		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"ORB personal para optimizaciones diversas.";
				Name										= "ORBITECH";
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
			}
			else if (State == State.Configure)
			{
				AddDataSeries(Data.BarsPeriodType.Tick, 1);
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			
		}

		protected override void OnBarUpdate()
		{
			//Add your custom strategy logic here.
		}
		
		#region Properties
		
		// === Inputs ===
	    [NinjaScriptProperty]
	    public int StartHour { get; set; } = 9;
	    [NinjaScriptProperty]
	    public int StartMinute { get; set; } = 30;
	
	    [NinjaScriptProperty]
	    public int EndHour { get; set; } = 9;
	    [NinjaScriptProperty]
	    public int EndMinute { get; set; } = 45;
	
	    [NinjaScriptProperty]
	    public int LimitHour { get; set; } = 10;
	    [NinjaScriptProperty]
	    public int LimitMinute { get; set; } = 0;
	
	    [NinjaScriptProperty]
	    public double SlMultiplier { get; set; } = 1.0;
	    [NinjaScriptProperty]
	    public double TpRRR { get; set; } = 1.0;
	
	    [NinjaScriptProperty]
	    public bool UseBreakEven { get; set; } = true;
	    [NinjaScriptProperty]
	    public int BeTriggerTicks { get; set; } = 10;
	    [NinjaScriptProperty]
	    public int BeOffsetTicks { get; set; } = 1;
		
		#endregion
		
	}
}
