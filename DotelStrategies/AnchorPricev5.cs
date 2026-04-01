#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.Dotel
{
    public class AnchorPricev5 : Strategy
    {
        // ====== Inputs (Usuario) ======

        [NinjaScriptProperty]
        [Display(Name = "Cantidad (lotes/contratos)", GroupName = "01. Orden", Order = 0)]
        [Range(1, int.MaxValue)]
        public int UserQuantity { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Permitir COMPRAS (Long)", GroupName = "01. Orden", Order = 1)]
        public bool EnableLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Permitir VENTAS (Short)", GroupName = "01. Orden", Order = 2)]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Hora CHECK (decimal, múltiplo 0.25)", GroupName = "02. Horas", Order = 0)]
        public double CheckHourDecimal { get; set; } = 16.50;

        [NinjaScriptProperty]
        [Display(Name = "Hora ANCHOR (decimal, múltiplo 0.25)", GroupName = "02. Horas", Order = 1)]
        public double AnchorHourDecimal { get; set; } = 13.00;

        [NinjaScriptProperty]
        [Display(Name = "Distancia mínima (ticks) Anchor vs Actual", GroupName = "03. Reglas", Order = 0)]
        [Range(0, int.MaxValue)]
        public int MinDistanceTicks { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "StopLoss (ticks)", GroupName = "04. Salidas", Order = 0)]
        [Range(1, int.MaxValue)]
        public int StopLossTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "TakeProfit (ticks)", GroupName = "04. Salidas", Order = 1)]
        [Range(1, int.MaxValue)]
        public int TakeProfitTicks { get; set; } = 20;

        // ====== Estado interno ======
        private TimeSpan checkTime;
        private TimeSpan anchorTime;

        private int currentYyyyMmDd = -1;
        private bool tradedToday = false;
        private bool checkExecuted = false;

        // Si AnchorHour > CheckHour => el anchor corresponde al último día con datos antes del día actual
        private bool anchorUsesPreviousDataDay = false;

        private double anchorPrice = double.NaN;

        // Vencimiento: funciona hasta el 01/04/2026 (inclusive)
        private static readonly DateTime ExpirationDate = new DateTime(2026, 4, 1);
        private bool expirationPrinted = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                                     = "AnchorPricev5";
                Description                              = "v5: filtros Long/Short + Anchor del último día con datos si AnchorHour > CheckHour + expiración 01/04/2026.";
                Calculate                                = Calculate.OnBarClose;

                EntriesPerDirection                      = 1;
                EntryHandling                            = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy             = true;
                ExitOnSessionCloseSeconds                = 30;

                IsInstantiatedOnEachOptimizationIteration = false;

                StartBehavior                            = StartBehavior.WaitUntilFlat;
            }
            else if (State == State.Configure)
            {
                // Validaciones de horas y conversión a TimeSpan
                if (!TryParseQuarterHourDecimal(CheckHourDecimal, out checkTime, out string err1))
                    throw new Exception("CheckHourDecimal inválido: " + err1);

                if (!TryParseQuarterHourDecimal(AnchorHourDecimal, out anchorTime, out string err2))
                    throw new Exception("AnchorHourDecimal inválido: " + err2);

                if (StopLossTicks <= 0 || TakeProfitTicks <= 0)
                    throw new Exception("StopLossTicks y TakeProfitTicks deben ser > 0.");

                if (UserQuantity <= 0)
                    throw new Exception("UserQuantity debe ser >= 1.");

                // Regla solicitada:
                // Si AnchorHour > CheckHour => significa que el anchor es "del día anterior",
                // pero lo adaptamos a: "último día con datos antes del día actual".
                anchorUsesPreviousDataDay = anchorTime > checkTime;

                // Salidas por ticks
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            // ====== Expiración ======
            // Funciona hasta 01/04/2026 inclusive. Desde 02/04/2026 no hace nada.
            if (Time[0].Date > ExpirationDate)
            {
                if (!expirationPrinted)
                {
                    expirationPrinted = true;
                    Print($"{Time[0]} | Estrategia expirada. Válida hasta {ExpirationDate:dd/MM/yyyy} (inclusive). Contacte con el desarrollador: @isdotel en discord");
                }
                return;
            }

            // Reset diario (por fecha del bar)
            int barDate = ToDay(Time[0]);
            if (barDate != currentYyyyMmDd)
            {
                currentYyyyMmDd = barDate;
                tradedToday     = false;
                checkExecuted   = false;
                anchorPrice     = double.NaN;
            }

            if (tradedToday || checkExecuted)
                return;

            TimeSpan now = Time[0].TimeOfDay;

            // Ejecutar check a la hora de check (una sola vez)
            if (!checkExecuted && now >= checkTime)
            {
                checkExecuted = true;

                // 1) Resolver anchorPrice (mismo día o último día con datos anterior según regla)
                if (!TryResolveAnchorPrice(Time[0], out anchorPrice, out string anchorErr))
                {
                    Print($"{Time[0]} | No se pudo resolver AnchorPrice: {anchorErr}. Se omite el trade del día.");
                    return;
                }

                double currentPrice = Close[0];

                // 2) Filtro de distancia mínima en ticks
                double distanceTicks = Math.Abs(currentPrice - anchorPrice) / TickSize;
                if (distanceTicks < MinDistanceTicks)
                {
                    Print($"{Time[0]} | Distancia insuficiente: {distanceTicks:F2} ticks < MinDistanceTicks({MinDistanceTicks}). No trade.");
                    return;
                }

                int qty = UserQuantity;

                // 3) Dirección:
                // - Si AnchorPrice < Current => abrir VENTA
                // - Si AnchorPrice > Current => abrir COMPRA
                // - Si igual => no trade
                if (anchorPrice < currentPrice)
                {
                    if (!EnableShorts)
                    {
                        Print($"{Time[0]} | Señal SHORT pero EnableShorts=false. No trade.");
                        return;
                    }

                    EnterShort(qty, "AnchorShort");
                    tradedToday = true;
                    Print($"{Time[0]} | SHORT | Anchor={anchorPrice} Current={currentPrice} Dist={distanceTicks:F2} ticks Qty={qty} (Anchor {(anchorUsesPreviousDataDay ? "PREV_DATA_DAY" : "HOY")} {anchorTime})");
                }
                else if (anchorPrice > currentPrice)
                {
                    if (!EnableLongs)
                    {
                        Print($"{Time[0]} | Señal LONG pero EnableLongs=false. No trade.");
                        return;
                    }

                    EnterLong(qty, "AnchorLong");
                    tradedToday = true;
                    Print($"{Time[0]} | LONG | Anchor={anchorPrice} Current={currentPrice} Dist={distanceTicks:F2} ticks Qty={qty} (Anchor {(anchorUsesPreviousDataDay ? "PREV_DATA_DAY" : "HOY")} {anchorTime})");
                }
                else
                {
                    Print($"{Time[0]} | AnchorPrice == CurrentPrice. No trade.");
                }
            }
        }

        // ====== Anchor resolver (v5) ======

        /// <summary>
        /// Obtiene el Close del "primer bar en/tras la hora anchor" según:
        /// - Si AnchorHour <= CheckHour => anchor del MISMO día.
        /// - Si AnchorHour  > CheckHour => anchor del ÚLTIMO DÍA CON DATOS antes del día actual.
        /// </summary>
        private bool TryResolveAnchorPrice(DateTime checkBarTime, out double resolvedAnchorPrice, out string error)
        {
            resolvedAnchorPrice = double.NaN;
            error = null;

            DateTime checkDay = checkBarTime.Date;

            DateTime anchorDay;
            if (anchorUsesPreviousDataDay)
            {
                if (!TryGetPreviousDayWithData(checkDay, out anchorDay, out string prevErr))
                {
                    error = prevErr;
                    return false;
                }
            }
            else
            {
                anchorDay = checkDay;
            }

            DateTime targetAnchorDateTime = anchorDay.Add(anchorTime);

            // Necesitamos el "primer bar en/tras" targetAnchorDateTime.
            int barsAgo = FindFirstBarAtOrAfter(targetAnchorDateTime);
            if (barsAgo < 0)
            {
                error = $"No hay bar disponible en/tras {targetAnchorDateTime:yyyy-MM-dd HH:mm:ss}.";
                return false;
            }

            // Asegurar que el bar encontrado pertenece al anchorDay.
            // Si esto falla, suele ser porque faltan datos en esa fecha/hora.
            if (Time[barsAgo].Date != anchorDay)
            {
                error = $"El bar encontrado cae en {Time[barsAgo]:yyyy-MM-dd HH:mm:ss} y no en el día esperado {anchorDay:yyyy-MM-dd}. (¿falta histórico para esa sesión/hora?)";
                return false;
            }

            resolvedAnchorPrice = Close[barsAgo];
            return true;
        }

        /// <summary>
        /// Devuelve el último día (fecha) que tenga datos en el histórico antes de referenceDay.
        /// Ej: si referenceDay es lunes y no hay datos sábado/domingo, devolverá viernes.
        /// </summary>
        private bool TryGetPreviousDayWithData(DateTime referenceDay, out DateTime previousDayWithData, out string error)
        {
            previousDayWithData = DateTime.MinValue;
            error = null;

            // Buscamos cualquier bar cuya fecha sea < referenceDay.
            // Como recorremos desde barsAgo=0 hacia el pasado, el primero que cumpla será el "último día con datos".
            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                DateTime d = Time[barsAgo].Date;
                if (d < referenceDay)
                {
                    previousDayWithData = d;
                    return true;
                }
            }

            error = $"No se encontró ningún día con datos anterior a {referenceDay:yyyy-MM-dd}. (¿historial insuficiente?)";
            return false;
        }

        /// <summary>
        /// Devuelve barsAgo del primer bar cuyo Time[barsAgo] >= targetDateTime.
        /// Si no existe, devuelve -1.
        /// </summary>
        private int FindFirstBarAtOrAfter(DateTime targetDateTime)
        {
            // Si el target es "futuro" respecto al bar actual, no se puede
            if (Time[0] < targetDateTime)
                return -1;

            // Buscamos el punto donde pasamos de >= target a < target
            // y devolvemos el bar "más cercano" por arriba (en/tras).
            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                DateTime t = Time[barsAgo];

                if (t < targetDateTime)
                {
                    int prev = barsAgo - 1; // el anterior era >= target (si existe)
                    return (prev >= 0) ? prev : -1;
                }
            }

            // Si nunca encontramos t < target, incluso el bar más antiguo >= target
            return CurrentBar;
        }

        // ====== Helpers ======

        /// <summary>
        /// Valida y convierte una hora decimal en cuartos (0.25) a TimeSpan.
        /// Reglas: múltiplo de 0.25, min 0, max 23.75.
        /// Ej: 16.5 => 16:30, 16.25 => 16:15, 23.75 => 23:45
        /// </summary>
        private bool TryParseQuarterHourDecimal(double hourDecimal, out TimeSpan time, out string error)
        {
            time = TimeSpan.Zero;
            error = null;

            if (hourDecimal < 0 || hourDecimal > 23.75)
            {
                error = $"Debe estar entre 0 y 23.75. Recibido: {hourDecimal}";
                return false;
            }

            double quarters = hourDecimal / 0.25;
            double roundedQuarters = Math.Round(quarters);
            if (Math.Abs(quarters - roundedQuarters) > 1e-9)
            {
                error = $"Debe ser múltiplo de 0.25. Recibido: {hourDecimal}";
                return false;
            }

            int totalMinutes = (int)Math.Round(hourDecimal * 60.0); // 0.25h = 15m
            int hh = totalMinutes / 60;
            int mm = totalMinutes % 60;

            if (hh < 0 || hh > 23 || (mm != 0 && mm != 15 && mm != 30 && mm != 45))
            {
                error = $"Formato inválido tras conversión. Recibido: {hourDecimal} => {hh:D2}:{mm:D2}";
                return false;
            }

            time = new TimeSpan(hh, mm, 0);
            return true;
        }

        private int ToDay(DateTime time)
        {
            return time.Year * 10000 + time.Month * 100 + time.Day;
        }
    }
}