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
			}
			else if (State == State.Configure)
			{
				// AddDataSeries(Data.BarsPeriodType.Tick, 1);
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			
		}

		protected override void OnBarUpdate()
		{
			 // Asegúrate de que estás trabajando solo con la serie principal
			if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
				return;

			// Resetear variables al comenzar un nuevo día
			if (Bars.IsFirstBarOfSession)
			{
			rangeHigh = double.MinValue;
			rangeLow = double.MaxValue;
			rangeComplete = false;
			tradeTaken = false;
			}


			DateTime barTime = Times[0][0];

			if (IsInRangeTime(barTime))
				UpdateRange();

			if (!rangeComplete && barTime.TimeOfDay >= GetTime(EndHour, EndMinute).TimeOfDay)
				rangeComplete = true;

			if (!rangeComplete && barTime.TimeOfDay >= GetTime(EndHour, EndMinute).TimeOfDay)
			{
				rangeComplete = true;
				DrawRangeBox();  // ← Pintamos el rectángulo del rango
			}


			if (rangeComplete && !tradeTaken && barTime.TimeOfDay < GetTime(LimitHour, LimitMinute).TimeOfDay)
				TryEnterTrade();

			if (UseBreakEven && tradeTaken)
				ManageBreakEven();
		}


		// ===== FUNCIONES MODULARES ===
		#region Funciones Modulares
		

		private DateTime GetTime(int hour, int minute)
		{
			return new DateTime(Times[0][0].Year, Times[0][0].Month, Times[0][0].Day, hour, minute, 0);
		}

		private bool IsInRangeTime(DateTime time)
		{
			return time >= GetTime(StartHour, StartMinute) && time <= GetTime(EndHour, EndMinute);
		}

		private void UpdateRange()
		{
			rangeHigh = Math.Max(rangeHigh, High[1]);
			rangeLow = Math.Min(rangeLow, Low[1]);
		}
		
		private void TryEnterTrade()
		{
			double rangeSize = rangeHigh - rangeLow;
			int slTicks = (int)(rangeSize / TickSize * SlMultiplier);
			int tpTicks = (int)(slTicks * TpRRR);

			// Usamos el cierre confirmado de la vela anterior
			if (Close[1] > rangeHigh)
			{
				entryPrice = Close[1];  // tomamos el precio actual como entrada
				EnterLong("ORB Long");
				SetStopLoss("ORB Long", CalculationMode.Ticks, slTicks, false);
				SetProfitTarget("ORB Long", CalculationMode.Ticks, tpTicks);
				tradeTaken = true;
			}
			else if (Close[1] < rangeLow)
			{
				entryPrice = Close[1];
				EnterShort("ORB Short");
				SetStopLoss("ORB Short", CalculationMode.Ticks, slTicks, false);
				SetProfitTarget("ORB Short", CalculationMode.Ticks, tpTicks);
				tradeTaken = true;
			}
		}


		private void ManageBreakEven()
		{
			if (Position.MarketPosition == MarketPosition.Long)
			{
				if (Close[0] >= entryPrice + (BeTriggerTicks * TickSize))
				{
					SetStopLoss(CalculationMode.Price, entryPrice + (BeOffsetTicks * TickSize));
				}
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				if (Close[0] <= entryPrice - (BeTriggerTicks * TickSize))
				{
					SetStopLoss(CalculationMode.Price, entryPrice - (BeOffsetTicks * TickSize));
				}
			}
		}

		private void DrawRangeBox()
		{
			string tag = "ORBRange_" + Times[0][0].ToString("yyyyMMddHHmmss");

			Draw.Rectangle(
				this,
				tag,
				false,
				GetTime(StartHour, StartMinute),
				rangeHigh,
				GetTime(EndHour, EndMinute),
				rangeLow,
				Brushes.Transparent,
				Brushes.Blue,
				2
			);
		}


		#endregion


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
