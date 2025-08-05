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
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class IntrabarBase : Strategy
	{

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Calculate = Calculate.OnEachTick;
				Name = "IntrabarBase";
			}

			else if (State == State.Configure)
			{
				// Add a secondary bar series. 
				AddDataSeries(Data.BarsPeriodType.Tick, 1);
			}
		}

		protected override void OnBarUpdate()
		{
			// Logica de la serie principal
			if (BarsInProgress == 0)
			{

			}

			// Logica de la serie secundaria
			else if (BarsInProgress == 1)
			{

			}


		}

		//Funcion para convertir las horas y minutos a un objeto DateTime
		protected DateTime ConvertToDateTime(int hour, int minute)	
		{
			DateTime now = DateTime.Now;
			return new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
		}

		// Funcion para verificar si la hora actual esta dentro del rango especificado
		protected bool IsTimeInRange(DateTime start, DateTime end)
		{
			DateTime now = DateTime.Now;
			return now >= start && now <= end;
		}

		#region Properties

		//Recibe un parametro de tipo int llamado hora.
		[Range(0, 23), Display(Name = "HoraInicio", Order = 1, GroupName = "Rango")]
		public int Hour { get; set; }

		[Range(0, 59), Display(Name = "MinutosInicio", Order = 2, GroupName = "Rango")]
		public int Minute { get; set; }

		//Ahora lo mismo pero para el final.
		[Range(0, 23), Display(Name = "HoraFinal", Order = 3, GroupName = "Rango")]
		public int HourEnd { get; set; }

		[Range(0, 59), Display(Name = "MinutosFinal", Order = 4, GroupName = "Rango")]
		public int MinuteEnd { get; set; }

		#endregion
	}
}
