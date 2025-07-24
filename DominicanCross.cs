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
	public class DominicanCross : Strategy
	{
		
	    private SMA fastMA;
	    private SMA slowMA;
	    private ADX adx;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Cruce de medias con ADX.";
				Name = "DominicanCross";
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
				PeriodFastMA = 9;
				PeriodSlowMA = 21;
				PeriodADX = 14;
				LevelADX = 20;
				TargetDistance = 6;
				StopDistance = 4;
			}
			else if (State == State.Configure)
			{
				SetStopLoss(CalculationMode.Ticks, StopDistance);
				SetProfitTarget(CalculationMode.Ticks, TargetDistance);
			}
			else if (State == State.DataLoaded)
			{
				fastMA = SMA(PeriodFastMA);
				slowMA = SMA(PeriodSlowMA);
            	adx = ADX(PeriodADX);
	        }
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 21)
            return;

	        if (adx[0] < LevelADX && IsAdxRising())
				return;
	
	        // Entrada en largo
	        if (CrossAbove(fastMA, slowMA, 1) && Position.MarketPosition == MarketPosition.Flat)
	        {
	            EnterLong("LongScalp");
	            
	            // Dibujar triángulo hacia arriba debajo de la vela
	            Draw.TriangleUp(this, "Long"+CurrentBar, false, 0, Low[0] - TickSize * 2, Brushes.Green);
	        }
	        // Entrada en corto
	        else if (CrossBelow(fastMA, slowMA, 1) && Position.MarketPosition == MarketPosition.Flat)
	        {
	            EnterShort("ShortScalp");
	
	            // Dibujar triángulo hacia abajo encima de la vela
	            Draw.TriangleDown(this, "Short"+CurrentBar, false, 0, High[0] + TickSize * 2, Brushes.Red);
	        }
		}

		#region Funciones Propias
		private bool IsAdxRising()
		{
			if (CurrentBar < adxLookbackPeriod)
				return false;

			for (int i = 1; i <= adxLookbackPeriod; i++)
			{
				if (adx[i] >= adx[i - 1])
					continue;
				else
					return false;
			}

			return true;
		}
		#endregion

		
		#region Variables
		private int adxLookbackPeriod = 3; // puedes cambiarlo en la configuración
		#endregion

		#region Properties
		
		[Range(1, 20), NinjaScriptProperty]
		[Display(Name = "ADX Lookback Period", Description = "Número de barras para confirmar que el ADX está en ascenso", Order = 1, GroupName = "Filtros")]
		public int AdxLookbackPeriod
		{
		    get { return adxLookbackPeriod; }
		    set { adxLookbackPeriod = value; }
		} 

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="PeriodFastMA", Order=1, GroupName="Parameters")]
		public int PeriodFastMA
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="PeriodSlowMA", Order=2, GroupName="Parameters")]
		public int PeriodSlowMA
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="PeriodADX", Order=3, GroupName="Parameters")]
		public int PeriodADX
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="LevelADX", Order=4, GroupName="Parameters")]
		public int LevelADX
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="TargetDistance", Order=5, GroupName="Parameters")]
		public int TargetDistance
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="StopDistance", Order=6, GroupName="Parameters")]
		public int StopDistance
		{ get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BuyDraw
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SellDraw
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DrawSlowMA
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DrawFastMA
		{
			get { return Values[3]; }
		}
		#endregion

	}
}
