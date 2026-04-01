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
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
	public class EngulfN_ATR_ADX_Bot : Strategy
	{
		#region Enums
		public enum TradeDirectionMode
		{
			Both = 0,
			LongOnly = 1,
			ShortOnly = 2
		}
		#endregion

		#region Inputs

		// 1) Control del backtesting
		[NinjaScriptProperty]
		[Display(Name = "Imprimir fecha por día (Backtest)", Order = 1, GroupName = "01. Control Backtesting")]
		public bool PrintBacktestDay { get; set; }

		// 2) Cantidad de velas a envolver
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Velas a envolver (N)", Order = 5, GroupName = "02. Patrón Envolvente")]
		public int EngulfBars { get; set; }

		// 7) Filtro horario (HHmm)
		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Hora inicio (HHmm)", Order = 10, GroupName = "03. Horario")]
		public int StartTimeHHmm { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Hora fin (HHmm)", Order = 11, GroupName = "03. Horario")]
		public int EndTimeHHmm { get; set; }

		// 6) Filtro dirección
		[NinjaScriptProperty]
		[Display(Name = "Dirección operativa", Order = 20, GroupName = "04. Dirección")]
		public TradeDirectionMode Direction { get; set; }

		// 3) ADX
		[NinjaScriptProperty]
		[Range(0, 200)]
		[Display(Name = "Periodo ADX (0 = OFF)", Order = 30, GroupName = "05. Filtro ADX")]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "ADX mínimo (0 = no usar)", Order = 31, GroupName = "05. Filtro ADX")]
		public double AdxMin { get; set; }

		// 4) ATR Stop
		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "Periodo ATR", Order = 40, GroupName = "06. Stop ATR")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 100.0)]
		[Display(Name = "Multiplicador ATR", Order = 41, GroupName = "06. Stop ATR")]
		public double AtrMultiplier { get; set; }

		// Take Profit como múltiplo del SL (en ticks)
		[NinjaScriptProperty]
		[Range(0.01, 100.0)]
		[Display(Name = "TP múltiplo del SL (RR)", Order = 42, GroupName = "06. Stop ATR")]
		public double TakeProfitRR { get; set; }

		// 5) Riesgo y sizing dinámico
		[NinjaScriptProperty]
		[Range(0.01, double.MaxValue)]
		[Display(Name = "Riesgo $ por operación", Order = 50, GroupName = "07. Gestión de Riesgo")]
		public double RiskDollars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Contratos máximos", Order = 51, GroupName = "07. Gestión de Riesgo")]
		public int MaxContracts { get; set; }

		// 8) Modo ejecución
		[NinjaScriptProperty]
		[Display(Name = "Modo Tick-a-Tick (true=OnEachTick / false=OnBarClose)", Order = 60, GroupName = "08. Ejecución")]
		public bool UseTickMode { get; set; }

		#endregion

		#region Private fields
		private ATR atr;
		private ADX adx;

		private DateTime lastPrintedDate = Core.Globals.MinDate;

		private int startTimeHHmmss;
		private int endTimeHHmmss;

		private const string LongSignalName  = "LONG_ENGULF_N";
		private const string ShortSignalName = "SHORT_ENGULF_N";
		#endregion

		#region OnStateChange
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "EngulfN_ATR_ADX_Bot";
				Description = "Envolvente de N velas previas con SL ATR, TP = múltiplo del SL, ADX opcional, sizing por riesgo, filtro horario/dirección.";
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				IncludeCommission = true;

				Calculate = Calculate.OnBarClose;

				// Defaults
				PrintBacktestDay = true;

				EngulfBars = 3;

				StartTimeHHmm = 1530;
				EndTimeHHmm = 1730;

				Direction = TradeDirectionMode.Both;

				AdxPeriod = 14;
				AdxMin = 0.0;

				AtrPeriod = 14;
				AtrMultiplier = 2.0;

				TakeProfitRR = 1.0; // TP = 1R por defecto

				RiskDollars = 100.0;
				MaxContracts = 10;

				UseTickMode = false;
			}
			else if (State == State.Configure)
			{
				Calculate = UseTickMode ? Calculate.OnEachTick : Calculate.OnBarClose;

				startTimeHHmmss = HHmmToHHmmss(StartTimeHHmm);
				endTimeHHmmss   = HHmmToHHmmss(EndTimeHHmm);
			}
			else if (State == State.DataLoaded)
			{
				atr = ATR(AtrPeriod);

				if (AdxPeriod > 0)
					adx = ADX(AdxPeriod);
			}
		}
		#endregion

		#region OnBarUpdate
		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			if (CurrentBar < EngulfBars)
				return;

			PrintDayProgressIfNeeded();

			if (!IsWithinTradingHours())
				return;

			if (Position.MarketPosition != MarketPosition.Flat)
				return;

			if (!PassAdxFilter())
				return;

			int stopTicks = GetStopTicksFromAtr();
			if (stopTicks <= 0)
				return;

			int tpTicks = GetTakeProfitTicks(stopTicks);
			if (tpTicks <= 0)
				return;

			int qty = GetPositionSizeByRisk(stopTicks);
			if (qty <= 0)
				return;

			bool longSignal  = (Direction == TradeDirectionMode.Both || Direction == TradeDirectionMode.LongOnly)  && CheckLongEngulfNSignal(EngulfBars);
			bool shortSignal = (Direction == TradeDirectionMode.Both || Direction == TradeDirectionMode.ShortOnly) && CheckShortEngulfNSignal(EngulfBars);

			// Importante: SetStopLoss/SetProfitTarget deben setearse ANTES del entry para asociarse al signalName
			if (longSignal)
			{
				SetStopLoss(LongSignalName, CalculationMode.Ticks, stopTicks, false);
				SetProfitTarget(LongSignalName, CalculationMode.Ticks, tpTicks);
				EnterLong(qty, LongSignalName);
			}
			else if (shortSignal)
			{
				SetStopLoss(ShortSignalName, CalculationMode.Ticks, stopTicks, false);
				SetProfitTarget(ShortSignalName, CalculationMode.Ticks, tpTicks);
				EnterShort(qty, ShortSignalName);
			}
		}
		#endregion

		#region Pattern logic (N + engulf)
		private bool CheckLongEngulfNSignal(int n)
		{
			if (n < 1)
				return false;

			for (int i = 1; i <= n; i++)
				if (!IsBear(i))
					return false;

			if (!IsBull(0))
				return false;

			double prevMaxHigh = double.MinValue;
			double prevMinLow  = double.MaxValue;
			double sumRanges   = 0.0;

			for (int i = 1; i <= n; i++)
			{
				prevMaxHigh = Math.Max(prevMaxHigh, High[i]);
				prevMinLow  = Math.Min(prevMinLow,  Low[i]);
				sumRanges  += (High[i] - Low[i]);
			}

			bool engulfsRange = High[0] >= prevMaxHigh && Low[0] <= prevMinLow;
			if (!engulfsRange)
				return false;

			double r0 = High[0] - Low[0];
			return r0 >= sumRanges;
		}

		private bool CheckShortEngulfNSignal(int n)
		{
			if (n < 1)
				return false;

			for (int i = 1; i <= n; i++)
				if (!IsBull(i))
					return false;

			if (!IsBear(0))
				return false;

			double prevMaxHigh = double.MinValue;
			double prevMinLow  = double.MaxValue;
			double sumRanges   = 0.0;

			for (int i = 1; i <= n; i++)
			{
				prevMaxHigh = Math.Max(prevMaxHigh, High[i]);
				prevMinLow  = Math.Min(prevMinLow,  Low[i]);
				sumRanges  += (High[i] - Low[i]);
			}

			bool engulfsRange = High[0] >= prevMaxHigh && Low[0] <= prevMinLow;
			if (!engulfsRange)
				return false;

			double r0 = High[0] - Low[0];
			return r0 >= sumRanges;
		}

		private bool IsBull(int barsAgo) => Close[barsAgo] > Open[barsAgo];
		private bool IsBear(int barsAgo) => Close[barsAgo] < Open[barsAgo];
		#endregion

		#region ADX filter
		private bool PassAdxFilter()
		{
			if (AdxPeriod <= 0)
				return true;

			if (adx == null)
				adx = ADX(AdxPeriod);

			if (AdxMin <= 0.0)
				return true;

			return adx[0] >= AdxMin;
		}
		#endregion

		#region ATR Stop + TakeProfit + Position sizing
		private int GetStopTicksFromAtr()
		{
			if (atr == null)
				atr = ATR(AtrPeriod);

			double atrValue = atr[0];
			if (atrValue <= 0)
				return 0;

			double stopDistance = atrValue * AtrMultiplier;
			int stopTicks = (int)Math.Ceiling(stopDistance / TickSize);

			return Math.Max(1, stopTicks);
		}

		private int GetTakeProfitTicks(int stopTicks)
		{
			if (TakeProfitRR <= 0)
				return 0;

			// TP en ticks = SL_ticks * RR
			int tpTicks = (int)Math.Round(stopTicks * TakeProfitRR, MidpointRounding.AwayFromZero);
			return Math.Max(1, tpTicks);
		}

		private int GetPositionSizeByRisk(int stopTicks)
		{
			double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
			double riskPerContract = stopTicks * tickValue;

			if (riskPerContract <= 0)
				return 0;

			int qty = (int)Math.Floor(RiskDollars / riskPerContract);
			if (qty < 1)
				return 0;

			return Math.Min(qty, MaxContracts);
		}
		#endregion

		#region Time filter + Backtest progress
		private bool IsWithinTradingHours()
		{
			if (StartTimeHHmm == 0 && EndTimeHHmm == 0)
				return true;

			int now = ToTime(Time[0]); // HHmmss

			// Ventana normal
			if (startTimeHHmmss <= endTimeHHmmss)
				return now >= startTimeHHmmss && now <= endTimeHHmmss;

			// Ventana cruzando medianoche
			return (now >= startTimeHHmmss) || (now <= endTimeHHmmss);
		}

		private int HHmmToHHmmss(int hhmm)
		{
			int hh = hhmm / 100;
			int mm = hhmm % 100;

			if (hh < 0 || hh > 23) hh = 0;
			if (mm < 0 || mm > 59) mm = 0;

			return (hh * 10000) + (mm * 100);
		}

		private void PrintDayProgressIfNeeded()
		{
			if (!PrintBacktestDay)
				return;

			if (Bars.IsFirstBarOfSession)
			{
				DateTime d = Time[0].Date;
				if (d != lastPrintedDate)
				{
					lastPrintedDate = d;
					Print($"{Name} | Backtest día: {d:yyyy-MM-dd}");
				}
			}
		}
		#endregion
	}
}
