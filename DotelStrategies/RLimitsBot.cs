#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.Dotel;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
	public class RLimitsBot : Strategy
	{
		private const string LongSignalName = "RLimitsLong";
		private const string ShortSignalName = "RLimitsShort";

		private ATR		atr;
		private EMA		highEma;
		private EMA		lowEma;
		private LinReg	highLinReg;
		private LinReg	lowLinReg;
		private SMA		highSma;
		private SMA		lowSma;
		private RLimits	rlimits;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name									= "RLimitsBot";
				Description								= "Counter-trend strategy based on RLimits closes with dynamic risk-based position sizing.";
				Calculate								= Calculate.OnBarClose;
				EntriesPerDirection						= 1;
				EntryHandling							= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds				= 30;
				IsInstantiatedOnEachOptimizationIteration	= false;
				BarsRequiredToTrade						= 20;

				Period				= 10;
				AverageType			= RLimitsAverageType.SMA;
				AtrMultiplier		= 1.5;
				StopLossMultiplier	= 2.0;
				TargetRiskDollars	= 125.0;
				MaxRiskDollars		= 125.0;
				AllowLong			= true;
				AllowShort			= true;
			}
			else if (State == State.DataLoaded)
			{
				highSma		= SMA(High, Period);
				lowSma		= SMA(Low, Period);
				highEma		= EMA(High, Period);
				lowEma		= EMA(Low, Period);
				highLinReg	= LinReg(High, Period);
				lowLinReg	= LinReg(Low, Period);
				atr			= ATR(Period);

				rlimits = RLimits(Period, AverageType, AtrMultiplier, StopLossMultiplier, false);
				AddChartIndicator(rlimits);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBar < Math.Max(BarsRequiredToTrade, Period))
				return;

			double iHigh = GetAverageValue(true);
			double iLow = GetAverageValue(false);

			bool sellSignal = Close[1] <= rlimits.RLimitHigh[1] && Close[0] > rlimits.RLimitHigh[0];
			bool buySignal = Close[1] >= rlimits.RLimitLow[1] && Close[0] < rlimits.RLimitLow[0];

			if (Position.MarketPosition != MarketPosition.Flat)
				return;

			if (AllowShort && sellSignal)
				EnterCounterTrade(-1, iLow);
			else if (AllowLong && buySignal)
				EnterCounterTrade(1, iHigh);
		}

		private double GetAverageValue(bool useHigh)
		{
			switch (AverageType)
			{
				case RLimitsAverageType.EMA:
					return useHigh ? highEma[0] : lowEma[0];
				case RLimitsAverageType.LinearRegression:
					return useHigh ? highLinReg[0] : lowLinReg[0];
				default:
					return useHigh ? highSma[0] : lowSma[0];
			}
		}

		private void EnterCounterTrade(int direction, double takeProfitPrice)
		{
			double entryPrice = Close[0];
			double targetDistance = Math.Abs(takeProfitPrice - entryPrice);

			if (targetDistance < TickSize)
				return;

			if (direction > 0 && takeProfitPrice <= entryPrice)
				return;

			if (direction < 0 && takeProfitPrice >= entryPrice)
				return;

			double stopLossPrice = direction > 0
				? entryPrice - targetDistance * StopLossMultiplier
				: entryPrice + targetDistance * StopLossMultiplier;

			takeProfitPrice = Instrument.MasterInstrument.RoundToTickSize(takeProfitPrice);
			stopLossPrice = Instrument.MasterInstrument.RoundToTickSize(stopLossPrice);

			int stopLossTicks = Math.Max(1, (int)Math.Ceiling(Math.Abs(entryPrice - stopLossPrice) / TickSize));
			int quantity = CalculateRiskQuantity(stopLossTicks);

			if (quantity < 1)
				return;

			if (direction > 0)
			{
				SetStopLoss(LongSignalName, CalculationMode.Price, stopLossPrice, false);
				SetProfitTarget(LongSignalName, CalculationMode.Price, takeProfitPrice);
				EnterLong(quantity, LongSignalName);
			}
			else
			{
				SetStopLoss(ShortSignalName, CalculationMode.Price, stopLossPrice, false);
				SetProfitTarget(ShortSignalName, CalculationMode.Price, takeProfitPrice);
				EnterShort(quantity, ShortSignalName);
			}
		}

		private int CalculateRiskQuantity(int stopLossTicks)
		{
			double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
			double riskPerContract = stopLossTicks * tickValue;

			if (riskPerContract <= 0 || TargetRiskDollars <= 0 || MaxRiskDollars <= 0)
				return 0;

			int quantity = (int)Math.Floor(TargetRiskDollars / riskPerContract);
			double selectedRisk = quantity * riskPerContract;

			if (selectedRisk > MaxRiskDollars)
			{
				quantity = (int)Math.Floor(MaxRiskDollars / riskPerContract);
				selectedRisk = quantity * riskPerContract;
			}

			int nextQuantity = quantity + 1;
			double nextRisk = nextQuantity * riskPerContract;

			if (nextRisk <= MaxRiskDollars
				&& Math.Abs(TargetRiskDollars - nextRisk) < Math.Abs(TargetRiskDollars - selectedRisk))
				quantity = nextQuantity;

			return Math.Max(0, quantity);
		}

		#region Properties
		[Range(2, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Period", Description = "Period used for the initial limits and ATR.", GroupName = "RLimits", Order = 0)]
		public int Period { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Average Type", Description = "Average used to calculate the initial limits.", GroupName = "RLimits", Order = 1)]
		public RLimitsAverageType AverageType { get; set; }

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "ATR Multiplier", Description = "ATR multiplier used to expand the real limits.", GroupName = "RLimits", Order = 2)]
		public double AtrMultiplier { get; set; }

		[Range(0.1, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Stop Loss Multiplier", Description = "Stop loss distance relative to the take profit distance.", GroupName = "Risk", Order = 0)]
		public double StopLossMultiplier { get; set; }

		[Range(0.01, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Target Risk Dollars", Description = "Preferred dollar risk per trade.", GroupName = "Risk", Order = 1)]
		public double TargetRiskDollars { get; set; }

		[Range(0.01, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Max Risk Dollars", Description = "Maximum dollar risk allowed when rounding position size up.", GroupName = "Risk", Order = 2)]
		public double MaxRiskDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Long", GroupName = "Trade", Order = 0)]
		public bool AllowLong { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Short", GroupName = "Trade", Order = 1)]
		public bool AllowShort { get; set; }
		#endregion
	}
}
