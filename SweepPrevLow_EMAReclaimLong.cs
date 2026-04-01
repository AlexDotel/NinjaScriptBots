#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// ---------------------------------------------
// Estrategia: SweepPrevLow_EMAReclaimLong
// Entradas cuando:
// 1) En la sesión actual se barrió el mínimo del día previo (Low < PriorLow).
// 2) (Opcional) Recupera por encima del PriorLow.
// 3) El precio supera una sola EMA (por defecto 50). Puedes usar cruce o condición simple.
// Incluye SL/TP en ticks y opción de 1 trade por día.
// ---------------------------------------------

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SweepPrevLow_EMAReclaimLong : Strategy
    {
        // ----- Parámetros -----
        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "Periodo EMA", Order = 1, GroupName = "Medias")]
        public int EmaPeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Usar cruce (CrossAbove)", Order = 2, GroupName = "Medias")]
        public bool UseCrossAbove { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Requerir recuperación sobre Low previo", Order = 3, GroupName = "Señal")]
        public bool RequireReclaimAbovePriorLow { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "StopLoss (ticks)", Order = 1, GroupName = "Riesgo")]
        public int StopLossTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ProfitTarget (ticks)", Order = 2, GroupName = "Riesgo")]
        public int ProfitTargetTicks { get; set; } = 80;

        [NinjaScriptProperty]
        [Display(Name = "Una operación por día", Order = 3, GroupName = "Riesgo")]
        public bool OneTradePerDay { get; set; } = true;

        // ----- Internos -----
        private EMA ema;
        private PriorDayOHLC priorDay;
        private bool sweptPrevLowToday;   // ¿Se barrió el mínimo previo en esta sesión?
        private bool tradedToday;         // ¿Ya se operó hoy?

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                                = "SweepPrevLow_EMAReclaimLong";
                Calculate                           = Calculate.OnBarClose;
                EntriesPerDirection                 = 1;
                EntryHandling                       = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy        = true;
                ExitOnSessionCloseSeconds           = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                // Valores por defecto ya establecidos arriba
            }
            else if (State == State.Configure)
            {
                // Si operas en TF mayores y quieres confirmación intrabar, puedes añadir una serie de 1 minuto aquí.
                // AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                // Crear indicadores, pero NO leer índices aquí
                ema      = EMA(Close, EmaPeriod);
                priorDay = PriorDayOHLC(Close);

                AddChartIndicator(ema);

                // Variables de sesión; no usar Time/Times aquí para evitar out-of-range
                sweptPrevLowToday = false;
                tradedToday       = false;
            }
        }

        protected override void OnBarUpdate()
        {
            // Guardas de calentamiento:
            // 1) Suficientes barras para EMA
            if (CurrentBar < EmaPeriod)
                return;

            // 2) PriorDayOHLC necesita al menos un día previo; si no hay valor, salimos
            if (priorDay == null || priorDay.PriorLow == null)
                return;

            double priorLow = priorDay.PriorLow[0];
            if (priorLow <= 0 || double.IsNaN(priorLow) || double.IsInfinity(priorLow))
                return;

            // Reset por nueva sesión (más robusto que leer Times[] en OnStateChange)
            if (Bars.IsFirstBarOfSession)
            {
                sweptPrevLowToday = false;
                tradedToday       = false;
            }

            // 1) Detectar "barrido" del mínimo del día anterior
            if (!sweptPrevLowToday && Low[0] < priorLow)
                sweptPrevLowToday = true;

            // 2) (Opcional) requerir recuperación por encima del priorLow
            bool reclaimOk = !RequireReclaimAbovePriorLow || Close[0] > priorLow;

            // 3) Trigger de EMA: cruce o superación simple
            bool emaTrigger =
                UseCrossAbove
                ? CrossAbove(Close, ema, 1)
                : Close[0] > ema[0];

            // 4) Reglas de entrada
            if (sweptPrevLowToday
                && reclaimOk
                && emaTrigger
                && Position.MarketPosition == MarketPosition.Flat
                && (!OneTradePerDay || !tradedToday))
            {
                // Definir SL/TP antes de la entrada
                if (StopLossTicks > 0)
                    SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                if (ProfitTargetTicks > 0)
                    SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);

                EnterLong("Long_ReclaimPrevLow_EMA");
                tradedToday = true;
            }
        }
    }
}
