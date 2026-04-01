using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.Gui.Tools;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class VolumeBreakoutStrategy : Strategy
    {
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Volume Threshold", Order = 1, GroupName = "Parameters")]
        public int VolumeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", Order = 2, GroupName = "Parameters")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", Order = 3, GroupName = "Parameters")]
        public int ProfitTargetTicks { get; set; }

        // B: if zero -> disable BE, else value in ticks to activate BE
        [NinjaScriptProperty]
        [Display(Name = "BE Activation (ticks) - B", Order = 4, GroupName = "Parameters")]
        public int BEActivation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Offset (ticks)", Order = 5, GroupName = "Parameters")]
        public int BEOffsetTicks { get; set; }

        // Time filter (hours, decimals in 0.25 increments allowed)
        [NinjaScriptProperty]
        [Display(Name = "Start Time (0-23.75)", Order = 6, GroupName = "Parameters")]
        public double StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "End Time (0-23.75)", Order = 7, GroupName = "Parameters")]
        public double EndTime { get; set; }

        private bool beApplied = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Volume breakout: entra en la dirección de la vela que supera el volumen configurado. SL/TP en ticks. BE opcional.";
                Name = "VolumeBreakoutStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                IsInstantiatedOnEachOptimizationIteration = false;

                VolumeThreshold = 1000;
                StopLossTicks = 20;
                ProfitTargetTicks = 40;
                BEActivation = 0;
                BEOffsetTicks = 1;
                StartTime = 0.0;
                EndTime = 23.75;
            }

            if (State == State.Configure)
            {
                // Validate time inputs to 0.25 increments. If they are not, round to nearest 0.25.
                StartTime = Math.Round(StartTime * 4.0) / 4.0;
                EndTime = Math.Round(EndTime * 4.0) / 4.0;

                // Apply initial SL/TP (in ticks)
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            // Time filter check
            double currentHour = Time[0].Hour + Time[0].Minute / 60.0 + Time[0].Second / 3600.0;
            if (!IsWithinTimeFilter(currentHour))
                return;

            // Reset BE flag when flat
            if (Position.MarketPosition == MarketPosition.Flat)
                beApplied = false;

            // Check volume breakout on current bar
            if (Volume[0] > VolumeThreshold)
            {
                // Candle direction
                if (Close[0] > Open[0])
                {
                    // Long
                    if (Position.MarketPosition != MarketPosition.Long)
                        EnterLong("VolBreakLong");
                }
                else if (Close[0] < Open[0])
                {
                    // Short
                    if (Position.MarketPosition != MarketPosition.Short)
                        EnterShort("VolBreakShort");
                }
            }

            // Break-even logic
            if (BEActivation > 0 && Position.MarketPosition != MarketPosition.Flat && !beApplied)
            {
                double ticksSinceEntry = 0.0;
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ticksSinceEntry = (High[0] - Position.AveragePrice) / TickSize;
                    if (ticksSinceEntry >= BEActivation)
                    {
                        // Move stop to entry + BEOffsetTicks
                        SetStopLoss(CalculationMode.Ticks, BEOffsetTicks);
                        beApplied = true;
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    ticksSinceEntry = (Position.AveragePrice - Low[0]) / TickSize;
                    if (ticksSinceEntry >= BEActivation)
                    {
                        SetStopLoss(CalculationMode.Ticks, BEOffsetTicks);
                        beApplied = true;
                    }
                }
            }
        }

        private bool IsWithinTimeFilter(double currentHour)
        {
            // Handle wrap-around if EndTime < StartTime
            if (StartTime <= EndTime)
                return currentHour >= StartTime && currentHour <= EndTime;
            else
                return currentHour >= StartTime || currentHour <= EndTime;
        }
    }
}
