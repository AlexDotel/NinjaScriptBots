#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
#endregion

// Nota: Archivo sugerido: EngulfingVWAPADXBot.cs
namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
	public class EngulfingVWAPADXBot : Strategy
	{
		// Indicadores (instanciados 1 vez)
		private VWAP vwap;
		private ADX  adx;

		// Break-even control
		private bool  beApplied = false;
		private double entryPrice = 0.0;

		#region Inputs

		[NinjaScriptProperty]
		[Display(Name="Allow Longs", Order=1, GroupName="1) Direction")]
		public bool AllowLongs { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name="Allow Shorts", Order=2, GroupName="1) Direction")]
		public bool AllowShorts { get; set; } = true;

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name="Start Time (HHmm)", Order=1, GroupName="2) Time Filter")]
		public int StartTimeHHmm { get; set; } = 930;

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name="End Time (HHmm)", Order=2, GroupName="2) Time Filter")]
		public int EndTimeHHmm { get; set; } = 1600;

		[NinjaScriptProperty]
		[Display(Name="Use VWAP Filter", Order=1, GroupName="3) VWAP Filter")]
		public bool UseVwapFilter { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name="Invert VWAP Logic", Order=2, GroupName="3) VWAP Filter")]
		public bool InvertVwapLogic { get; set; } = false;

		[NinjaScriptProperty]
		[Display(Name="Use ADX Filter", Order=1, GroupName="4) ADX Filter")]
		public bool UseAdxFilter { get; set; } = false;

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name="ADX Period", Order=2, GroupName="4) ADX Filter")]
		public int AdxPeriod { get; set; } = 14;

		[NinjaScriptProperty]
		[Range(0.0, 100.0)]
		[Display(Name="ADX Min Level", Order=3, GroupName="4) ADX Filter")]
		public double AdxMinLevel { get; set; } = 20.0;

		[NinjaScriptProperty]
		[Range(0, 100000)]
		[Display(Name="Stop Loss (ticks)", Order=1, GroupName="5) Risk")]
		public int StopLossTicks { get; set; } = 20;

		[NinjaScriptProperty]
		[Range(0, 100000)]
		[Display(Name="Take Profit (ticks)", Order=2, GroupName="5) Risk")]
		public int TakeProfitTicks { get; set; } = 20;

		[NinjaScriptProperty]
		[Display(Name="Use Break Even", Order=1, GroupName="6) Break Even")]
		public bool UseBreakEven { get; set; } = false;

		[NinjaScriptProperty]
		[Range(1, 100000)]
		[Display(Name="BE Trigger (ticks)", Order=2, GroupName="6) Break Even")]
		public int BreakEvenTriggerTicks { get; set; } = 10;

		[NinjaScriptProperty]
		[Range(0, 100000)]
		[Display(Name="BE Offset (ticks)", Order=3, GroupName="6) Break Even")]
		public int BreakEvenOffsetTicks { get; set; } = 1;

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "EngulfingVWAPADXBot";
				Calculate = Calculate.OnBarClose;	// Backtest más rápido
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsInstantiatedOnEachOptimizationIteration = false; // más rápido en optimizaciones
				BarsRequiredToTrade = 20;

				// Importante: para evitar comportamientos raros en histórico
				OrderFillResolution = OrderFillResolution.Standard;
			}
			else if (State == State.DataLoaded)
			{
				// Indicadores 1 vez
				if (UseVwapFilter)
					vwap = VWAP();

				if (UseAdxFilter)
					adx = ADX(AdxPeriod);

				// SL/TP por defecto (en ticks)
				if (StopLossTicks > 0)
					SetStopLoss(CalculationMode.Ticks, StopLossTicks);

				if (TakeProfitTicks > 0)
					SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			// 1) Gestión BE (si hay posición abierta)
			ManageBreakEven();

			// 2) Solo señales en horario
			if (!IsWithinTradingHours(Time[0]))
				return;

			// 3) No re-entrar si ya hay posición
			if (Position.MarketPosition != MarketPosition.Flat)
				return;

			// 4) Señal envolvente
			bool bullEngulf = IsBullishEngulfing();
			bool bearEngulf = IsBearishEngulfing();

			// 5) Filtros VWAP / ADX
			bool passAdx  = PassAdxFilter();
			bool passLong = PassVwapForLong();
			bool passShort= PassVwapForShort();

			// 6) Entradas
			if (AllowLongs && bullEngulf && passAdx && passLong)
			{
				EnterLong("Long_Engulf");
			}
			else if (AllowShorts && bearEngulf && passAdx && passShort)
			{
				EnterShort("Short_Engulf");
			}
		}

		#region Logic Helpers

		private bool IsWithinTradingHours(DateTime barTime)
		{
			// Convierte la hora actual a HHmm
			int hhmm = barTime.Hour * 100 + barTime.Minute;

			// Ventana normal (Start <= End)
			if (StartTimeHHmm <= EndTimeHHmm)
				return hhmm >= StartTimeHHmm && hhmm <= EndTimeHHmm;

			// Ventana cruzando medianoche (ej. 2200 -> 0200)
			return (hhmm >= StartTimeHHmm) || (hhmm <= EndTimeHHmm);
		}

		private bool PassAdxFilter()
		{
			if (!UseAdxFilter)
				return true;

			// adx se crea solo si UseAdxFilter=true en DataLoaded
			return adx != null && adx[0] >= AdxMinLevel;
		}

		private bool PassVwapForLong()
		{
			if (!UseVwapFilter)
				return true;

			if (vwap == null)
				return true;

			bool priceAbove = Close[0] > vwap[0];

			// Lógica normal: long cuando por encima
			// Invertida: long cuando por debajo
			return InvertVwapLogic ? !priceAbove : priceAbove;
		}

		private bool PassVwapForShort()
		{
			if (!UseVwapFilter)
				return true;

			if (vwap == null)
				return true;

			bool priceBelow = Close[0] < vwap[0];

			// Lógica normal: short cuando por debajo
			// Invertida: short cuando por encima
			return InvertVwapLogic ? !priceBelow : priceBelow;
		}

		private bool IsBullishEngulfing()
		{
			// Requiere 2 velas: [1] previa, [0] actual
			// Envolvente alcista típica:
			// - Vela previa bajista (Close[1] < Open[1])
			// - Vela actual alcista (Close[0] > Open[0])
			// - Cuerpo actual envuelve el cuerpo previo:
			//     Open[0] <= Close[1]  y  Close[0] >= Open[1]
			if (Close[1] >= Open[1]) return false;
			if (Close[0] <= Open[0]) return false;

			return Open[0] <= Close[1] && Close[0] >= Open[1];
		}

		private bool IsBearishEngulfing()
		{
			// Envolvente bajista típica:
			// - Vela previa alcista
			// - Vela actual bajista
			// - Cuerpo actual envuelve el cuerpo previo:
			//     Open[0] >= Close[1]  y  Close[0] <= Open[1]
			if (Close[1] <= Open[1]) return false;
			if (Close[0] >= Open[0]) return false;

			return Open[0] >= Close[1] && Close[0] <= Open[1];
		}

		private void ManageBreakEven()
		{
			if (!UseBreakEven)
				return;

			// Reset si estamos flat
			if (Position.MarketPosition == MarketPosition.Flat)
			{
				beApplied = false;
				entryPrice = 0.0;
				return;
			}

			// Captura entry una vez
			if (entryPrice == 0.0)
				entryPrice = Position.AveragePrice;

			if (beApplied)
				return;

			double triggerPriceDist = BreakEvenTriggerTicks * TickSize;

			if (Position.MarketPosition == MarketPosition.Long)
			{
				double move = Close[0] - entryPrice;
				if (move >= triggerPriceDist)
				{
					// Stop a entry + offset
					double newStop = entryPrice + (BreakEvenOffsetTicks * TickSize);
					SetStopLoss(CalculationMode.Price, newStop);
					beApplied = true;
				}
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				double move = entryPrice - Close[0];
				if (move >= triggerPriceDist)
				{
					// Stop a entry - offset
					double newStop = entryPrice - (BreakEvenOffsetTicks * TickSize);
					SetStopLoss(CalculationMode.Price, newStop);
					beApplied = true;
				}
			}
		}

		#endregion
	}
}