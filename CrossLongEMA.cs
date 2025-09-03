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
	public class CrossLongEMA : Strategy
	{
		private Indicators.CandleStickPatternLogic candleStickPatternLogic;
		
		protected override void OnStateChange()
		{
			base.OnStateChange();

			if (State == State.SetDefaults)
			{
				IsInstantiatedOnEachOptimizationIteration = false;
				IncludeTradeHistoryInBacktest             = false;
				IsExitOnSessionCloseStrategy              = false;
				IsInstantiatedOnEachOptimizationIteration = true;
				MaximumBarsLookBack                       = MaximumBarsLookBack.TwoHundredFiftySix;
				Name                                      = "CrossLongEMA";
				SupportsOptimizationGraph                 = false;
				SmaPeriod                                 = 10; // Valor predeterminado para los períodos de la SMA
				EmaPeriodForSL                            = 10; // Valor predeterminado para los períodos de la EMA de SL
			}
			else if (State == State.Configure)
			{
				candleStickPatternLogic = new CandleStickPatternLogic(this, 6);
			}
			else if (State == State.DataLoaded)
			{
				AddChartIndicator(SMA(SmaPeriod)); // Usar el período configurable para la SMA
				AddChartIndicator(EMA(EmaPeriodForSL)); // Usar el período configurable para la EMA de SL
			}
		}
	
		protected override void OnBarUpdate()
		{
			base.OnBarUpdate();

			if (CurrentBars[0] < BarsRequiredToTrade)
				return;

			// Lógica de entrada con SMA configurable
			if (CrossAbove(Close, SMA(SmaPeriod), 1))
				EnterLong();

			// Si estamos en largo, colocar una orden de Exit Stop en el precio de la EMA configurable
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLongStopMarket(EMA(EmaPeriodForSL)[0]);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "SmaPeriod", Order = 1, GroupName = "Parameters")]
		public int SmaPeriod
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EmaPeriodForSL", Order = 2, GroupName = "Parameters")]
		public int EmaPeriodForSL
		{ get; set; }
		#endregion
	}
}
