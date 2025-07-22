//
// Copyright (C) 2024, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
	public class Auto01 : Strategy
	{
		private Indicators.CandleStickPatternLogic candleStickPatternLogic;
		
		protected override void OnStateChange()
		{
			base.OnStateChange();

			if (State == State.SetDefaults)
			{
				IncludeTradeHistoryInBacktest             = false;
				IsInstantiatedOnEachOptimizationIteration = true;
				MaximumBarsLookBack                       = MaximumBarsLookBack.TwoHundredFiftySix;
				Name                                      = "Auto01";
				SupportsOptimizationGraph                 = false;
			}
			else if (State == State.Configure)
			{
				candleStickPatternLogic = new CandleStickPatternLogic(this, 3);
			}
			else if (State == State.DataLoaded)
			{
				AddChartIndicator(CandlestickPattern(ChartPattern.ThreeBlackCrows, 3));
				AddChartIndicator(CandlestickPattern(ChartPattern.BearishHarami, 3));
				AddChartIndicator(CandlestickPattern(ChartPattern.UpsideGapTwoCrows, 3));
				AddChartIndicator(MFI(14));
				AddChartIndicator(RSI(14, 3));
				AddChartIndicator(NBarsUp(3, true, true, true));
				AddChartIndicator(RSS(10, 40, 5));
			}
		}

		protected override void OnBarUpdate()
		{
			base.OnBarUpdate();

			if (CurrentBars[0] < BarsRequiredToTrade)
				return;

			if (candleStickPatternLogic.Evaluate(ChartPattern.ThreeBlackCrows))
				EnterShort();

			if ((candleStickPatternLogic.Evaluate(ChartPattern.BearishHarami)
				&& !((candleStickPatternLogic.Evaluate(ChartPattern.UpsideGapTwoCrows)
						&& MFI(14)[0].ApproxCompare(28) == 0))))
				ExitShort();
		}
	}
}
