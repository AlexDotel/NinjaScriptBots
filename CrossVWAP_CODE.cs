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
	public class CrossVWAP_Code : Strategy
	{
		private bool Crossed;
		private bool CanTrade;
		private double TrailPrice;

		private Brush Brush1;
		private ADX ADX1;
		private ADX ADX2;
		private ADX ADX3;

		private VWAP VWAP1;
		private TSSuperTrend TSSuperTrend1;
		private TSSuperTrend TSSuperTrend2;

    private double initialStopPrice;

		protected override void OnStateChange()
    {
      if (State == State.SetDefaults)
      {
        Description = @"";
        Name = "CrossVWAP_Code";
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
        IsInstantiatedOnEachOptimizationIteration = false;
        // StopSize					= 25;
        // TargetSize					= 25;
        // Compras = true;
        // Ventas = true;
        StartHour = 14;
        StartMinute = 30;
        EndHour = 21;
        EndMinute = 30;
        ADX_Level = 25;
        CanTrade = false;
        TrailPrice = 1;
        ADX_Period = 14; // Valor predeterminado para el período del ADX
        ADXCheckBars = 3; // Número de barras para verificar la tendencia del ADX
        InitialStopTicks = 100; // Valor predeterminado para el SL inicial en ticks
      }
      else if (State == State.Configure)
      {
        Brush1 = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF403F45"));
        Brush1.Freeze();
      }
      else if (State == State.DataLoaded)
      {
        ADX1 = ADX(Close, ADX_Period); // Usar el período como input
        ADX2 = ADX(Close, (ADX_Period)); // ADX de 5 mins (En caso de usar velas de minutos)
        ADX3 = ADX(Close, (ADX_Period)); // ADX de 15 mins (En caso de usar velas de minutos)
        VWAP1 = VWAP(Close);
        TSSuperTrend1 = TSSuperTrend(Close, SuperTrendMode.ATR, 42, 2.5, MovingAverageType.HMA, 42, false, false, false);
        TSSuperTrend2 = TSSuperTrend(Close, SuperTrendMode.ATR, 42, 2.5, MovingAverageType.HMA, 42, true, false, false);
        VWAP1.Plots[0].Brush = Brushes.Aqua;
        VWAP1.Plots[1].Brush = Brushes.Transparent;
        VWAP1.Plots[2].Brush = Brushes.Transparent;
        VWAP1.Plots[3].Brush = Brushes.Transparent;
        VWAP1.Plots[4].Brush = Brushes.Transparent;
        VWAP1.Plots[5].Brush = Brushes.Transparent;
        VWAP1.Plots[6].Brush = Brushes.Transparent;
        VWAP1.Plots[7].Brush = Brushes.Transparent;
        VWAP1.Plots[8].Brush = Brushes.Transparent;
        VWAP1.Plots[9].Brush = Brushes.Transparent;
        VWAP1.Plots[10].Brush = Brushes.Transparent;
        TSSuperTrend2.Plots[0].Brush = Brushes.Green;
        TSSuperTrend2.Plots[1].Brush = Brushes.Red;
        AddChartIndicator(VWAP1);
        AddChartIndicator(TSSuperTrend2);
      }
    }

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			CanTrade = false;
			if (CurrentBars[0] < 1)
				return;


			DateTime startTime = new DateTime(Times[0][0].Year, Times[0][0].Month, Times[0][0].Day, StartHour, StartMinute, 0);
			DateTime endTime = new DateTime(Times[0][0].Year, Times[0][0].Month, Times[0][0].Day, EndHour, EndMinute, 0);

			// Set 2
			if ((Times[0][0] >= startTime)
				 && (Times[0][0] <= endTime)
				 && (IsADXAscending(ADX1, ADXCheckBars))
				 && (IsADXAscending(ADX2, ADXCheckBars))
				 && (IsADXAscending(ADX3, ADXCheckBars)))
			{
				CanTrade = true;
				BackBrush = Brush1;
			}
			
			 // Set 3
			if ((CanTrade == true)
				 && (Position.MarketPosition == MarketPosition.Flat)
				 && (CrossAbove(Close, VWAP1.PlotVWAP1U, 1))
				 && (Close[0] > TSSuperTrend1.UpTrend[0]))
			{
				EnterLong(Convert.ToInt32(DefaultQuantity), @"Long");

				// Establecer un stop loss inicial basado en el input de ticks
				initialStopPrice = Close[0] - (InitialStopTicks * TickSize);
				ExitLongStopMarket(Convert.ToInt32(DefaultQuantity), initialStopPrice, @"InitialStop", @"Long");
			}
			
			 // Set 4
			if (Position.MarketPosition == MarketPosition.Long)
			{
				TrailPrice = TSSuperTrend2.UpTrend[0];

				// Activar el trailing stop solo si es más corto que el SL inicial
				if (TrailPrice > initialStopPrice)
				{
					ExitLongStopMarket(Convert.ToInt32(DefaultQuantity), TrailPrice, @"TrailLong", @"Long");
				}
			}
		}

		// Función para verificar si el ADX está en ascenso en las últimas 'bars' velas
		private bool IsADXAscending(ADX adx, int bars)
		{
			// Validar que hay suficientes barras para realizar la comprobación
			if (CurrentBars[1] < bars)
				return false;

			for (int i = 0; i < bars - 1; i++)
			{
				if (adx[1 + i] <= adx[2 + i])
					return false;
			}
			return true;
		}

		#region Properties
		// [NinjaScriptProperty]
		// [Range(1, int.MaxValue)]
		// [Display(Name="StopSize", Order=1, GroupName="Parameters")]
		// public int StopSize
		// { get; set; }

		// [NinjaScriptProperty]
		// [Range(1, int.MaxValue)]
		// [Display(Name="TargetSize", Order=2, GroupName="Parameters")]
		// public int TargetSize
		// { get; set; }

		// [NinjaScriptProperty]
		// [Display(Name="Compras", Order=3, GroupName="Parameters")]
		// public bool Compras
		// { get; set; }

		// [NinjaScriptProperty]
		// [Display(Name="Ventas", Order=4, GroupName="Parameters")]
		// public bool Ventas
		// { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name="StartHour", Order=5, GroupName="Parameters")]
		public int StartHour
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name="StartMinute", Order=6, GroupName="Parameters")]
		public int StartMinute
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name="EndHour", Order=7, GroupName="Parameters")]
		public int EndHour
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name="EndMinute", Order=8, GroupName="Parameters")]
		public int EndMinute
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADX_Level", Order=9, GroupName="Parameters")]
		public int ADX_Level
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADX_Period", Order=10, GroupName="Parameters")]
		public int ADX_Period
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADXCheckBars", Order=11, GroupName="Parameters")]
		public int ADXCheckBars
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="InitialStopTicks", Order=12, GroupName="Parameters")]
		public int InitialStopTicks
		{ get; set; }
		#endregion

	}
}
