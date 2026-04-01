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

// Folder/namespace: Dotel
namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
	public class RSIDOTBOT : Strategy
	{
		private EMA ema;
		private RSI rsi;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name					= "RSIDOTBOT";
				Calculate				= Calculate.OnBarClose;
				EntriesPerDirection		= 1;
				EntryHandling			= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds	= 30;
				IsInstantiatedOnEachOptimizationIteration = true;

				// --- Trading window (decimal hours, quarter-hour steps) ---
				StartHourDec				= 9.25;   // 09:15
				EndHourDec					= 16.00;  // 16:00

				// --- Toggles ---
				EnableLongs				= true;
				EnableShorts			= true;

				// --- EMA filter ---
				EmaPeriod				= 200;

				// --- RSI ---
				RsiPeriod				= 2;
				RsiSmoothing			= 1;

				// Entries
				LongEntryLevel			= 20;
				ShortEntryLevel			= 80;

				// Exits (dynamic via RSI)
				LongExitHighLevel		= 70;  // exit long if RSI crosses ABOVE this
				LongExitLowLevel		= 10;  // exit long if RSI crosses BELOW this
				LongExitExtremeLow		= 0;   // exit long if RSI <= this

				ShortExitLowLevel		= 20;  // exit short if RSI crosses BELOW this
				ShortExitHighLevel		= 100; // exit short if RSI >= this
			}
			else if (State == State.Configure)
			{
				// nothing
			}
			else if (State == State.DataLoaded)
			{
				if (!ValidateQuarterHour(StartHourDec) || !ValidateQuarterHour(EndHourDec))
				{
					Print("RSIDOTBOT: StartHourDec/EndHourDec must be in [0, 23.75] and multiples of 0.25 (15 minutes). Disabling strategy.");
					IsEnabled = false;
					return;
				}

				if (EmaPeriod < 1 || RsiPeriod < 1 || RsiSmoothing < 1)
				{
					Print("RSIDOTBOT: Invalid indicator periods. Disabling strategy.");
					IsEnabled = false;
					return;
				}

				ema = EMA(EmaPeriod);
				rsi = RSI(RsiPeriod, RsiSmoothing);

				AddChartIndicator(ema);
				AddChartIndicator(rsi);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Math.Max(EmaPeriod, RsiPeriod) + 5)
				return;

			// Time filter
			if (!IsWithinTradingWindow(Time[0], StartHourDec, EndHourDec))
				return;

			double emaVal = ema[0];
			double rsiVal = rsi[0];

			bool aboveEma = Close[0] > emaVal;
			bool belowEma = Close[0] < emaVal;

			// --- Exits first (dynamic) ---
			if (Position.MarketPosition == MarketPosition.Long)
			{
				bool exitLong =
					CrossAbove(rsi, LongExitHighLevel, 1) ||
					CrossBelow(rsi, LongExitLowLevel, 1) ||
					rsiVal <= LongExitExtremeLow;

				if (exitLong)
					ExitLong("ExitLong_RSI", "Long_RSI");
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				bool exitShort =
					CrossBelow(rsi, ShortExitLowLevel, 1) ||
					rsiVal >= ShortExitHighLevel;

				if (exitShort)
					ExitShort("ExitShort_RSI", "Short_RSI");
			}

			// --- Entries ---
			if (Position.MarketPosition == MarketPosition.Flat)
			{
				// Long: price above EMA AND RSI crosses BELOW entry level (e.g., 20)
				if (EnableLongs && aboveEma && CrossBelow(rsi, LongEntryLevel, 1))
				{
					EnterLong(1, "Long_RSI");
				}

				// Short: price below EMA AND RSI crosses ABOVE entry level (e.g., 80)
				if (EnableShorts && belowEma && CrossAbove(rsi, ShortEntryLevel, 1))
				{
					EnterShort(1, "Short_RSI");
				}
			}
		}

		// =========================
		// Helpers: time + validation
		// =========================

		private bool ValidateQuarterHour(double hourDec)
		{
			if (hourDec < 0 || hourDec > 23.75)
				return false;

			// Convert to minutes and check multiple of 15
			double minutes = hourDec * 60.0;
			double rounded = Math.Round(minutes);

			// tolerance for floating input
			if (Math.Abs(minutes - rounded) > 1e-6)
				return false;

			return ((int)rounded % 15) == 0;
		}

		private int HourDecToMinutes(double hourDec)
		{
			// Assumes validated
			return (int)Math.Round(hourDec * 60.0);
		}

		private bool IsWithinTradingWindow(DateTime barTime, double startHourDec, double endHourDec)
		{
			int startMin = HourDecToMinutes(startHourDec);
			int endMin   = HourDecToMinutes(endHourDec);

			int curMin = barTime.Hour * 60 + barTime.Minute;

			// Normal window (same day): start <= end
			if (startMin <= endMin)
				return curMin >= startMin && curMin <= endMin;

			// Overnight window (wrap): e.g., 20:00 -> 03:00
			return curMin >= startMin || curMin <= endMin;
		}

		// =========================
		// Inputs
		// =========================

		[NinjaScriptProperty]
		[Display(Name = "StartHourDec", GroupName = "Time Filter", Order = 0)]
		public double StartHourDec { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EndHourDec", GroupName = "Time Filter", Order = 1)]
		public double EndHourDec { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableLongs", GroupName = "Toggles", Order = 0)]
		public bool EnableLongs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableShorts", GroupName = "Toggles", Order = 1)]
		public bool EnableShorts { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EmaPeriod", GroupName = "EMA", Order = 0)]
		public int EmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "RsiPeriod", GroupName = "RSI", Order = 0)]
		public int RsiPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "RsiSmoothing", GroupName = "RSI", Order = 1)]
		public int RsiSmoothing { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "LongEntryLevel", GroupName = "RSI Levels - Entries", Order = 0)]
		public double LongEntryLevel { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "ShortEntryLevel", GroupName = "RSI Levels - Entries", Order = 1)]
		public double ShortEntryLevel { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "LongExitHighLevel", GroupName = "RSI Levels - Exits", Order = 0)]
		public double LongExitHighLevel { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "LongExitLowLevel", GroupName = "RSI Levels - Exits", Order = 1)]
		public double LongExitLowLevel { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "LongExitExtremeLow", GroupName = "RSI Levels - Exits", Order = 2)]
		public double LongExitExtremeLow { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "ShortExitLowLevel", GroupName = "RSI Levels - Exits", Order = 3)]
		public double ShortExitLowLevel { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "ShortExitHighLevel", GroupName = "RSI Levels - Exits", Order = 4)]
		public double ShortExitHighLevel { get; set; }
	}
}
