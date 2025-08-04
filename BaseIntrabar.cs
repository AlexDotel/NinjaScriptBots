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
	public class BaseIntrabar : Strategy
	{
		Order buyOrder;
		Order sellOrder;
			
		#region Properties
		#endregion

		protected override void OnStateChange()
		{
			
			if (State == State.SetDefaults)
			{
				Description = @"BaseIntrabar";
				Name = "BaseIntrabar";
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
			}
			else if (State == State.Configure)
			{

				AddDataSeries(Data.BarsPeriodType.Tick, 1);

				SetProfitTarget("BuyStop", CalculationMode.Ticks, TakeProfitTicks);
				SetStopLoss("BuyStop", CalculationMode.Ticks, StopLossTicks, false);

				SetProfitTarget("SellStop", CalculationMode.Ticks, TakeProfitTicks);
				SetStopLoss("SellStop", CalculationMode.Ticks, StopLossTicks, false);

			}
		}

		private TimeSpan ConvertToTimeSpan(int hour, int minute)
		{
			return new TimeSpan(hour, minute, 0);
		}

		private bool ordersPlaced = false;
		private bool tradingCompletedForTheDay = false;
		private double ghostBuyPrice;
		private double ghostSellPrice;

		protected override void OnBarUpdate()
		{
			// Ensure we are in the correct data series
			if (BarsInProgress != 1)
			{
				Print($"[{Time[0]}] Skipping update. Not in the correct data series.");
				return;
			}

			// Ensure there are enough bars to access Time[1]
			if (CurrentBar < 1)
			{
				Print($"[{Time[0]}] Skipping update. Not enough bars loaded.");
				return;
			}

			// Reset tradingCompletedForTheDay and ordersPlaced at the start of a new session
			if (Bars.IsFirstBarOfSession)
			{
				tradingCompletedForTheDay = false; // Allow trading for the new session
				ordersPlaced = false; // Reset ordersPlaced for the new session
				Print($"[{Time[0]}] New session started. Resetting tradingCompletedForTheDay and ordersPlaced.");

				// Close any open positions from the previous session
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					ExitLong("CloseLong", "BuyMarket");
					ExitShort("CloseShort", "SellMarket");
					Print($"[{Time[0]}] Closing open positions from the previous session.");
				}
			}

			// Ensure we have no active positions or orders
			if (Position.MarketPosition != MarketPosition.Flat || tradingCompletedForTheDay)
			{
				Print($"[{Time[0]}] Skipping update. MarketPosition: {Position.MarketPosition}, ordersPlaced: {ordersPlaced}, tradingCompletedForTheDay: {tradingCompletedForTheDay}");
				return;
			}

			TimeSpan tradeTime = ConvertToTimeSpan(TradeHour, TradeMinute);
			TimeSpan limitTime = ConvertToTimeSpan(LimitHour, LimitMinute);

			// Check if it's the trade time and orders are not yet placed
			if (Time[0].TimeOfDay >= tradeTime && Time[0].TimeOfDay < limitTime && !ordersPlaced)
			{
				ghostBuyPrice = Close[0] + TicksOffset * TickSize;
				ghostSellPrice = Close[0] - TicksOffset * TickSize;

				// Print trade time and calculated prices
				Print($"[{Time[0]}] Trade Time: {Time[0].TimeOfDay}, Limit Time: {limitTime}");
				Print($"[{Time[0]}] Actual Price: {Close[0]}, Buy Price: {ghostBuyPrice}, Sell Price: {ghostSellPrice}");

				ordersPlaced = true;
				Print($"[{Time[0]}] Orders placed. Waiting for price to reach ghost levels.");
			}

			// Check if the price reaches the ghost order levels
			if (ordersPlaced)
			{
				if (Close[0] >= ghostBuyPrice)
				{
					EnterLong(1, OrderSize, "BuyMarket");
					ordersPlaced = false; // Reset ordersPlaced after execution
					tradingCompletedForTheDay = true; // Mark trading as completed for the day
					Print($"[{Time[0]}] Entered long position at market price. Trading completed for the day.");
				}
				else if (Close[0] <= ghostSellPrice)
				{
					EnterShort(1, OrderSize, "SellMarket");
					ordersPlaced = false; // Reset ordersPlaced after execution
					tradingCompletedForTheDay = true; // Mark trading as completed for the day
					Print($"[{Time[0]}] Entered short position at market price. Trading completed for the day.");
				}
				else if (Time[0].TimeOfDay >= limitTime)
				{
					ordersPlaced = false; // Reset ordersPlaced if limit time is reached
					Print($"[{Time[0]}] Limit time reached. No orders executed. Resetting ordersPlaced.");
				}
			}

			// Ensure no further trading occurs after the first trade of the day
			if (tradingCompletedForTheDay)
			{
				Print($"[{Time[0]}] Trading already completed for the day. Skipping further updates.");
				return;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "TradeHour", Order = 1, GroupName = "Trade Settings")]
		public int TradeHour { get; set; } = 15;

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name="TradeMinute", Order=2, GroupName="Trade Settings")]
		public int TradeMinute { get; set; } = 30;

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name="LimitHour", Order=3, GroupName="Trade Settings")]
		public int LimitHour { get; set; } = 15;

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name="LimitMinute", Order=4, GroupName="Trade Settings")]
		public int LimitMinute { get; set; } = 50;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="TicksOffset", Order=5, GroupName="Trade Settings")]
		public int TicksOffset { get; set; } = 61;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="TakeProfitTicks", Order=6, GroupName="Trade Settings")]
		public int TakeProfitTicks { get; set; } = 62;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="StopLossTicks", Order=7, GroupName="Trade Settings")]
		public int StopLossTicks { get; set; } = 51;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="OrderSize", Order=8, GroupName="Trade Settings")]
		public int OrderSize { get; set; } = 10;
		#endregion
	}
}
