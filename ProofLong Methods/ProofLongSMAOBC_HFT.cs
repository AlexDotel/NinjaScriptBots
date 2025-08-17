#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies.OnEachTickStrats
{
	public class ProofLongSMAOBC_HFT : Strategy
	{
		private RSI rsiIndicator;
		private ADX adxIndicator;

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
				Description                         = @"ProofLongSMAOBC_HFT";
				Name                                = "ProofLongSMAOBC_HFT";
				Calculate                           = Calculate.OnBarClose;   // run on each incoming tick (driven by the 1-tick series)
				EntriesPerDirection                 = 1;
				EntryHandling                       = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy        = true;
				ExitOnSessionCloseSeconds           = 30;
				IsFillLimitOnTouch                  = false;
				MaximumBarsLookBack                 = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution                 = OrderFillResolution.Standard;
				Slippage                            = 0;
				StartBehavior                       = StartBehavior.WaitUntilFlat;
				TimeInForce                         = TimeInForce.Day;
				TraceOrders                         = false;
				RealtimeErrorHandling               = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling                  = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade                 = 20;
				IsInstantiatedOnEachOptimizationIteration = true;
			}
			else if (State == State.Configure)
			{
				// Add a 1-tick series to drive intrabar OnBarUpdate calls.
				AddDataSeries(BarsPeriodType.Tick, 1);

				// Targets & stops defined once; valid for Analyzer and live/backtest
				SetProfitTarget(CalculationMode.Ticks, TakeProfit);
				SetStopLoss(CalculationMode.Ticks, StopLoss);

				// Parse hours if enabled
				if (EnableTimeFilter)
				{
					try
					{
						allowedHoursList = AllowedHours.Split(',')
							.Select(s => s.Trim())
							.Where(s => s.Length > 0)
							.Select(s => int.Parse(s))
							.Where(h => h >= 0 && h <= 23)
							.Distinct()
							.ToList();
						if (allowedHoursList.Count == 0)
							throw new Exception("No valid hours provided.");
					}
					catch (Exception ex)
					{
						Log($"Invalid AllowedHours format: {AllowedHours}. Error: {ex.Message}", LogLevel.Error);
						allowedHoursList = null; // disable filter if invalid
					}
				}
				else
				{
					allowedHoursList = null;
				}
			}
			else if (State == State.DataLoaded)
			{
				// Create indicators on the PRIMARY series (BarsArray[0])
				rsiIndicator = RSI(BarsArray[0], 14, 3);
				adxIndicator = ADX(BarsArray[0], 14);

				// AddChartIndicator is safe on charts; Strategy Analyzer will just ignore visuals
				try
				{
					AddChartIndicator(rsiIndicator);
					AddChartIndicator(adxIndicator);
				}
				catch { /* Analyzer context may not support chart visuals; ignore */ }
			}
		}

		protected override void OnBarUpdate()
		{
			// Only drive logic on the 1-tick series for intrabar responsiveness
			if (BarsInProgress != 1)
				return;

			// Ensure both series have enough bars
			if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < 1)
				return;

			// Ensure enough history for indicator lookbacks and the ADX rising check
			int minPrimaryBars = Math.Max(BarsRequiredToTrade, 14 + AdxRisingBars + 1);
			if (CurrentBars[0] < minPrimaryBars)
				return;

			// Time filter uses the PRIMARY series session time for consistency
			if (EnableTimeFilter && allowedHoursList != null && allowedHoursList.Count > 0)
			{
				int hour = Times[0][0].Hour;
				if (!allowedHoursList.Contains(hour))
					return;
			}

			// ADX rising filter, checking that the last 'AdxRisingBars' bars are strictly increasing
			bool isAdxRising = true;
			for (int i = 0; i < AdxRisingBars; i++)
			{
				// Compare newer bar (lower index) to older bar (higher index)
				if (adxIndicator[i] <= adxIndicator[i + 1])
				{
					isAdxRising = false;
					break;
				}
			}
			if (!isAdxRising)
				return;

			// Entry condition uses last closed primary bar values for stability
			double rsiPrev = rsiIndicator[1];
			double adxPrev = adxIndicator[1];

			if (rsiPrev > 70 && adxPrev >= 25 && adxPrev <= 35)
			{
				// Only place a new entry if flat to avoid over-ordering on every tick within the bar
				if (Position.MarketPosition == MarketPosition.Flat)
					EnterLong();
			}
		}
	}
}
