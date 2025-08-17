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
namespace NinjaTrader.NinjaScript.Strategies.OnEachTickStrats
{
	public class ProofLongSMA : Strategy
	{
		private Indicators.RSI rsiIndicator;
		private Indicators.ADX adxIndicator;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Take Profit (TP)", Order = 1, GroupName = "Parameters")]
		public int TakeProfit { get; set; } = 35;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop Loss (SL)", Order = 2, GroupName = "Parameters")]
		public int StopLoss { get; set; } = 29;

		[NinjaScriptProperty]
		[Display(Name = "Enable Time Filter", Order = 3, GroupName = "Parameters")]
		public bool EnableTimeFilter { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name = "Allowed Hours (comma-separated)", Order = 4, GroupName = "Parameters")]
		public string AllowedHours { get; set; } = "15,16,17,18";

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ADX Rising Bars", Order = 5, GroupName = "Parameters")]
		public int AdxRisingBars { get; set; } = 3;

		private List<int> allowedHoursList;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Proof";
				Name										= "ProofLongSMA";
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
				TimeInForce									= TimeInForce.Day;
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
				rsiIndicator = RSI(14, 3);
				adxIndicator = ADX(14);
				AddChartIndicator(rsiIndicator);
				AddChartIndicator(adxIndicator);
				SetProfitTarget(CalculationMode.Ticks, TakeProfit);
				SetStopLoss(CalculationMode.Ticks, StopLoss);
				if (EnableTimeFilter)
				{
					try
					{
						allowedHoursList = AllowedHours.Split(',')
							.Select(hour => int.TryParse(hour.Trim(), out var h) ? h : -1)
							.Where(h => h >= 0 && h <= 23)
							.ToList();

						if (allowedHoursList.Count == 0)
							throw new Exception("No valid hours provided.");
					}
					catch (Exception ex)
					{
						Log($"Invalid AllowedHours format: {AllowedHours}. Error: {ex.Message}", LogLevel.Error);
						allowedHoursList = new List<int>(); // Reset to avoid runtime errors
					}
				}
				else
				{
					allowedHoursList = null; // Disable time filtering
				}
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < BarsRequiredToTrade)
				return;

			// Time filter logic
			if (EnableTimeFilter && allowedHoursList != null && !allowedHoursList.Contains(Time[0].Hour))
				return;

			// ADX rising filter logic
			bool isAdxRising = true;
			for (int i = 1; i <= AdxRisingBars; i++)
			{
				if (adxIndicator[i] < adxIndicator[i + 1])
				{
					isAdxRising = false;
					break;
				}
			}

			if (!isAdxRising)
				return;

			// Add your custom strategy logic here.
			if (rsiIndicator[1] > 70 && adxIndicator[1] >= 25 && adxIndicator[1] <= 35)
				EnterLong();
		}
	}
}
