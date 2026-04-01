//
// Copyright (C) 2025, NinjaTrader LLC <www.ninjatrader.com>.
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
	public class AutoStrategy : Strategy
	{
		private Indicators.CandleStickPatternLogic candleStickPatternLogic;
		
		protected override void OnStateChange()
		{
			base.OnStateChange();

			if (State == State.SetDefaults)
			{
				IncludeTradeHistoryInBacktest             = false;
				IsExitOnSessionCloseStrategy              = false;
				IsInstantiatedOnEachOptimizationIteration = true;
				MaximumBarsLookBack                       = MaximumBarsLookBack.TwoHundredFiftySix;
				Name                                      = "AutoStrategy";
				SupportsOptimizationGraph                 = false;
			}
			else if (State == State.Configure)
			{
				candleStickPatternLogic = new CandleStickPatternLogic(this, 2);
				SetParabolicStop(CalculationMode.Percent, 0.0075);
			}
			else if (State == State.DataLoaded)
			{
				AddChartIndicator(CandlestickPattern(ChartPattern.BearishEngulfing, 2));
			}
		}

		protected override void OnBarUpdate()
		{
			base.OnBarUpdate();

			if (CurrentBars[0] < BarsRequiredToTrade)
				return;

			if (candleStickPatternLogic.Evaluate(ChartPattern.BearishEngulfing))
				EnterShort();
		}
	}
}
