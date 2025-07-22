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
	public class FVW : Strategy
	{
		private bool CanTrade;
		private int GapSize;

		private Brush Brush1;
		private VWAP VWAP1;
		private Series<double> StopSizeSerie;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Aprovechamos los FVG con direccion del vwap.";
				Name										= "FVW";
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
				STime						= DateTime.Parse("15:30", System.Globalization.CultureInfo.InvariantCulture);
				Etime						= DateTime.Parse("17:30", System.Globalization.CultureInfo.InvariantCulture);
				StopLoss					= 100;
				TakeProfit					= 100;
				SizeFVG					= 10;
				TrailingTrigger					= 25;
				CanTrade					= false;
				GapSize					= 0;
			}
			else if (State == State.Configure)
			{
				Brush1 = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D2D2F"));
				Brush1.Freeze();
			}
			else if (State == State.DataLoaded)
			{				
				StopSizeSerie = new Series<double>(this);			
				VWAP1				= VWAP(Close);
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
				AddChartIndicator(VWAP1);
				SetProfitTarget(@"SHORT", CalculationMode.Ticks, TakeProfit);
				SetProfitTarget(@"LONG", CalculationMode.Ticks, TakeProfit);
				SetStopLoss(@"SHORT", CalculationMode.Ticks, StopLoss, false);
				SetStopLoss(@"LONG", CalculationMode.Currency, StopLoss, false);
				SetTrailStop(@"LONG", CalculationMode.Percent, TrailingTrigger, false);
				SetTrailStop(@"SHORT", CalculationMode.Percent, TrailingTrigger, false);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			CanTrade = false;
			if (CurrentBars[0] < 3)
				return;

			 // Set 2
			if ((Times[0][0].TimeOfDay >= STime.TimeOfDay)
				 && (Times[0][0].TimeOfDay <= Etime.TimeOfDay))
			{
				CanTrade = true;
				BackBrush = Brush1;
			}
			
			 // Set 3
			if (((GetCurrentAsk(0) + 2)  < VWAP1.PlotVWAP[0])
				 && (CanTrade == true)
				 // Custom FVG B
				 && ((Low[3] - (High[1]))  >= SizeFVG))
			{
				Draw.ArrowDown(this, @"FVW Arrow down_1 " + Convert.ToString(CurrentBars[0]) + " " + Convert.ToString(Times[0][0].TimeOfDay), false, 2, (High[2] + 5) , Brushes.Red);
				EnterShort(Convert.ToInt32(DefaultQuantity), @"SHORT");
				GapSize = Convert.ToInt32((Low[3] - (High[1])) );
				Draw.Text(this, @"FVW Text_1 " + Convert.ToString(CurrentBars[0]), Convert.ToString(GapSize), 0, (High[2] + 100) );
			}
			
			 // Set 4
			if (((GetCurrentAsk(0) + 2)  > VWAP1.PlotVWAP[0])
				 && (CanTrade == true)
				 // Custom FVG A
				 && ((Low[1] - (High[3]))  >= SizeFVG))
			{
				Draw.ArrowUp(this, @"FVW Arrow up_1 " + Convert.ToString(CurrentBars[0]), false, 2, Low[2], Brushes.Lime);
				EnterLong(Convert.ToInt32(DefaultQuantity), @"LONG");
				GapSize = Convert.ToInt32((Low[1] - (High[3])) );
				Draw.Text(this, @"FVW Text_1 " + Convert.ToString(CurrentBars[0]), Convert.ToString(GapSize), 0, (Low[2] - 100) );
			}
			
		}

		#region Properties
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="STime", Order=1, GroupName="Parameters")]
		public DateTime STime
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="Etime", Order=2, GroupName="Parameters")]
		public DateTime Etime
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="StopLoss", Order=3, GroupName="Parameters")]
		public int StopLoss
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="TakeProfit", Order=4, GroupName="Parameters")]
		public int TakeProfit
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="SizeFVG", Order=5, GroupName="Parameters")]
		public int SizeFVG
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="TrailingTrigger", Description="Porciento de la operacion para activar el trailing.", Order=6, GroupName="Parameters")]
		public int TrailingTrigger
		{ get; set; }
		#endregion

	}
}

#region Wizard settings, neither change nor remove
/*@
<?xml version="1.0"?>
<ScriptProperties xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Calculate>OnBarClose</Calculate>
  <ConditionalActions>
    <ConditionalAction>
      <Actions>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Set CanTrade</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-08-19T19:35:08.6564006</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2024-08-19T19:35:08.6564006</VariableDateTime>
            <VariableBool>false</VariableBool>
          </ActionProperties>
          <ActionType>SetValue</ActionType>
          <UserVariableType>bool</UserVariableType>
          <VariableName>CanTrade</VariableName>
        </WizardAction>
      </Actions>
      <ActiveAction>
        <Children />
        <IsExpanded>false</IsExpanded>
        <IsSelected>true</IsSelected>
        <Name>Set CanTrade</Name>
        <OffsetType>Arithmetic</OffsetType>
        <ActionProperties>
          <DashStyle>Solid</DashStyle>
          <DivideTimePrice>false</DivideTimePrice>
          <Id />
          <File />
          <IsAutoScale>false</IsAutoScale>
          <IsSimulatedStop>false</IsSimulatedStop>
          <IsStop>false</IsStop>
          <LogLevel>Information</LogLevel>
          <Mode>Currency</Mode>
          <OffsetType>Currency</OffsetType>
          <Priority>Medium</Priority>
          <Quantity>
            <DefaultValue>0</DefaultValue>
            <IsInt>true</IsInt>
            <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
            <DynamicValue>
              <Children />
              <IsExpanded>false</IsExpanded>
              <IsSelected>false</IsSelected>
              <Name>Default order quantity</Name>
              <OffsetType>Arithmetic</OffsetType>
              <AssignedCommand>
                <Command>DefaultQuantity</Command>
                <Parameters />
              </AssignedCommand>
              <BarsAgo>0</BarsAgo>
              <CurrencyType>Currency</CurrencyType>
              <Date>2024-08-19T19:35:08.6564006</Date>
              <DayOfWeek>Sunday</DayOfWeek>
              <EndBar>0</EndBar>
              <ForceSeriesIndex>false</ForceSeriesIndex>
              <LookBackPeriod>0</LookBackPeriod>
              <MarketPosition>Long</MarketPosition>
              <Period>0</Period>
              <ReturnType>Number</ReturnType>
              <StartBar>0</StartBar>
              <State>Undefined</State>
              <Time>0001-01-01T00:00:00</Time>
            </DynamicValue>
            <IsLiteral>false</IsLiteral>
            <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
          </Quantity>
          <ServiceName />
          <ScreenshotPath />
          <SoundLocation />
          <TextPosition>BottomLeft</TextPosition>
          <VariableDateTime>2024-08-19T19:35:08.6564006</VariableDateTime>
          <VariableBool>false</VariableBool>
        </ActionProperties>
        <ActionType>SetValue</ActionType>
        <UserVariableType>bool</UserVariableType>
        <VariableName>CanTrade</VariableName>
      </ActiveAction>
      <AnyOrAll>All</AnyOrAll>
      <Conditions />
      <SetName>Set 1</SetName>
      <SetNumber>1</SetNumber>
    </ConditionalAction>
    <ConditionalAction>
      <Actions>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Set CanTrade</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-08-19T19:35:47.0872646</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2024-08-19T19:35:47.0872646</VariableDateTime>
            <VariableBool>true</VariableBool>
          </ActionProperties>
          <ActionType>SetValue</ActionType>
          <UserVariableType>bool</UserVariableType>
          <VariableName>CanTrade</VariableName>
        </WizardAction>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Set background color</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <Brush xsi:type="SolidColorBrush">
              <Opacity>1</Opacity>
              <Transform xsi:type="MatrixTransform">
                <Matrix>
                  <M11>1</M11>
                  <M12>0</M12>
                  <M21>0</M21>
                  <M22>1</M22>
                  <OffsetX>0</OffsetX>
                  <OffsetY>0</OffsetY>
                </Matrix>
              </Transform>
              <RelativeTransform xsi:type="MatrixTransform">
                <Matrix>
                  <M11>1</M11>
                  <M12>0</M12>
                  <M21>0</M21>
                  <M22>1</M22>
                  <OffsetX>0</OffsetX>
                  <OffsetY>0</OffsetY>
                </Matrix>
              </RelativeTransform>
              <Color>
                <A>255</A>
                <R>45</R>
                <G>45</G>
                <B>47</B>
                <ScA>1</ScA>
                <ScR>0.0262412224</ScR>
                <ScG>0.0262412224</ScG>
                <ScB>0.02842604</ScB>
              </Color>
            </Brush>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-08-23T20:26:38.9119659</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2024-08-23T20:26:38.9119659</VariableDateTime>
            <VariableBool>false</VariableBool>
          </ActionProperties>
          <ActionType>SetValue</ActionType>
          <UserVariableType>brush</UserVariableType>
          <VariableName>BackBrush</VariableName>
        </WizardAction>
      </Actions>
      <AnyOrAll>All</AnyOrAll>
      <Conditions>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Time series</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>Times[{0}][{1}].TimeOfDay</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-08-19T19:35:20.30395</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>true</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Time</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>GreaterEqual</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>STime</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>STime.TimeOfDay</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-08-19T19:35:20.3159486</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Time</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>Times[Default input][0].TimeOfDay &gt;= STime.TimeOfDay</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Time series</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>Times[{0}][{1}].TimeOfDay</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-08-19T19:35:20.30395</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>true</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Time</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>LessEqual</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Etime</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>Etime.TimeOfDay</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2024-08-24T00:49:55.5679726</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Time</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>Times[Default input][0].TimeOfDay &lt;= Etime.TimeOfDay</DisplayName>
        </WizardConditionGroup>
      </Conditions>
      <SetName>Set 2</SetName>
      <SetNumber>2</SetNumber>
    </ConditionalAction>
    <ConditionalAction>
      <Actions>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Arrow down</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <BarsAgo>2</BarsAgo>
            <Brush xsi:type="SolidColorBrush">
              <Opacity>1</Opacity>
              <Transform xsi:type="MatrixTransform">
                <Matrix>
                  <M11>1</M11>
                  <M12>0</M12>
                  <M21>0</M21>
                  <M22>1</M22>
                  <OffsetX>0</OffsetX>
                  <OffsetY>0</OffsetY>
                </Matrix>
              </Transform>
              <RelativeTransform xsi:type="MatrixTransform">
                <Matrix>
                  <M11>1</M11>
                  <M12>0</M12>
                  <M21>0</M21>
                  <M22>1</M22>
                  <OffsetX>0</OffsetX>
                  <OffsetY>0</OffsetY>
                </Matrix>
              </RelativeTransform>
              <Color>
                <A>255</A>
                <R>255</R>
                <G>0</G>
                <B>0</B>
                <ScA>1</ScA>
                <ScR>1</ScR>
                <ScG>0</ScG>
                <ScB>0</ScB>
              </Color>
            </Brush>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T00:16:43.1354954</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>FVW</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Index>1</Index>
                  <StringValue>Arrow down_1</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Action>
                    <Children />
                    <IsExpanded>false</IsExpanded>
                    <IsSelected>true</IsSelected>
                    <Name>Current bar</Name>
                    <OffsetType>Arithmetic</OffsetType>
                    <AssignedCommand>
                      <Command>CurrentBars[{0}]</Command>
                      <Parameters>
                        <string>Series1</string>
                      </Parameters>
                    </AssignedCommand>
                    <BarsAgo>0</BarsAgo>
                    <CurrencyType>Currency</CurrencyType>
                    <Date>2025-05-31T00:12:54.814518</Date>
                    <DayOfWeek>Sunday</DayOfWeek>
                    <EndBar>0</EndBar>
                    <ForceSeriesIndex>true</ForceSeriesIndex>
                    <LookBackPeriod>0</LookBackPeriod>
                    <MarketPosition>Long</MarketPosition>
                    <Period>0</Period>
                    <ReturnType>Number</ReturnType>
                    <StartBar>0</StartBar>
                    <State>Undefined</State>
                    <Time>0001-01-01T00:00:00</Time>
                  </Action>
                  <Index>2</Index>
                  <StringValue>CurrentBars[0]</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Action>
                    <Children />
                    <IsExpanded>false</IsExpanded>
                    <IsSelected>true</IsSelected>
                    <Name>Time series</Name>
                    <OffsetType>Arithmetic</OffsetType>
                    <AssignedCommand>
                      <Command>Times[{0}][{1}].TimeOfDay</Command>
                      <Parameters>
                        <string>Series1</string>
                        <string>BarsAgo</string>
                      </Parameters>
                    </AssignedCommand>
                    <BarsAgo>0</BarsAgo>
                    <CurrencyType>Currency</CurrencyType>
                    <Date>2025-05-31T00:13:09.7355538</Date>
                    <DayOfWeek>Sunday</DayOfWeek>
                    <EndBar>0</EndBar>
                    <ForceSeriesIndex>true</ForceSeriesIndex>
                    <LookBackPeriod>0</LookBackPeriod>
                    <MarketPosition>Long</MarketPosition>
                    <Period>0</Period>
                    <ReturnType>Time</ReturnType>
                    <StartBar>0</StartBar>
                    <State>Undefined</State>
                    <Time>0001-01-01T00:00:00</Time>
                  </Action>
                  <Index>3</Index>
                  <StringValue>Times[0][0].TimeOfDay</StringValue>
                </NinjaScriptString>
              </Strings>
            </Tag>
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2025-05-31T00:16:43.1354954</VariableDateTime>
            <VariableBool>false</VariableBool>
            <Y>
              <DefaultValue>0</DefaultValue>
              <IsInt>false</IsInt>
              <BindingValue xsi:type="xsd:string">(High[2] + 5) </BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>High</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>2</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T00:13:49.9519492</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Add</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">5</BindingValue>
                    <IsLiteral>true</IsLiteral>
                    <LiveValue xsi:type="xsd:string">5</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>High</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">(High[2] + 5) </LiveValue>
            </Y>
          </ActionProperties>
          <ActionType>Drawing</ActionType>
          <Command>
            <Command>ArrowDown</Command>
            <Parameters>
              <string>owner</string>
              <string>tag</string>
              <string>isAutoScale</string>
              <string>barsAgo</string>
              <string>y</string>
              <string>brush</string>
            </Parameters>
          </Command>
        </WizardAction>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Enter short position</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:28:22.6986889</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SignalName>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>SHORT</StringValue>
                </NinjaScriptString>
              </Strings>
            </SignalName>
            <SoundLocation />
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2025-05-31T02:28:32.0723026</VariableDateTime>
            <VariableBool>false</VariableBool>
          </ActionProperties>
          <ActionType>Enter</ActionType>
          <Command>
            <Command>EnterShort</Command>
            <Parameters>
              <string>quantity</string>
              <string>signalName</string>
            </Parameters>
          </Command>
        </WizardAction>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Set GapSize</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:57:07.4298246</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <TextPosition>BottomLeft</TextPosition>
            <VariableInt>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">(Low[3] - (High[1])) </BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Low</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>3</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:57:13.742561</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Subtract</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">High[1]</BindingValue>
                    <DynamicValue>
                      <Children />
                      <IsExpanded>false</IsExpanded>
                      <IsSelected>true</IsSelected>
                      <Name>High</Name>
                      <OffsetType>Arithmetic</OffsetType>
                      <AssignedCommand>
                        <Command>{0}</Command>
                        <Parameters>
                          <string>Series1</string>
                          <string>BarsAgo</string>
                          <string>OffsetBuilder</string>
                        </Parameters>
                      </AssignedCommand>
                      <BarsAgo>1</BarsAgo>
                      <CurrencyType>Currency</CurrencyType>
                      <Date>2025-05-31T17:57:22.2030938</Date>
                      <DayOfWeek>Sunday</DayOfWeek>
                      <EndBar>0</EndBar>
                      <ForceSeriesIndex>false</ForceSeriesIndex>
                      <LookBackPeriod>0</LookBackPeriod>
                      <MarketPosition>Long</MarketPosition>
                      <Period>0</Period>
                      <ReturnType>Series</ReturnType>
                      <Series1>
                        <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                        <CustomProperties />
                        <IsExplicitlyNamed>false</IsExplicitlyNamed>
                        <IsPriceTypeLocked>true</IsPriceTypeLocked>
                        <PlotOnChart>false</PlotOnChart>
                        <PriceType>High</PriceType>
                        <SeriesType>DefaultSeries</SeriesType>
                      </Series1>
                      <StartBar>0</StartBar>
                      <State>Undefined</State>
                      <Time>0001-01-01T00:00:00</Time>
                    </DynamicValue>
                    <IsLiteral>false</IsLiteral>
                    <LiveValue xsi:type="xsd:string">High[1]</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>Low</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">(Low[3] - (High[1])) </LiveValue>
            </VariableInt>
            <VariableDateTime>2025-05-31T17:57:07.4298246</VariableDateTime>
            <VariableBool>false</VariableBool>
          </ActionProperties>
          <ActionType>SetValue</ActionType>
          <UserVariableType>int</UserVariableType>
          <VariableName>GapSize</VariableName>
        </WizardAction>
        <WizardAction>
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Text</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T21:05:31.7995009</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>FVW</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Index>1</Index>
                  <StringValue>Text_1</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Action>
                    <Children />
                    <IsExpanded>false</IsExpanded>
                    <IsSelected>true</IsSelected>
                    <Name>Current bar</Name>
                    <OffsetType>Arithmetic</OffsetType>
                    <AssignedCommand>
                      <Command>CurrentBars[{0}]</Command>
                      <Parameters>
                        <string>Series1</string>
                      </Parameters>
                    </AssignedCommand>
                    <BarsAgo>0</BarsAgo>
                    <CurrencyType>Currency</CurrencyType>
                    <Date>2025-05-31T17:44:48.6414848</Date>
                    <DayOfWeek>Sunday</DayOfWeek>
                    <EndBar>0</EndBar>
                    <ForceSeriesIndex>true</ForceSeriesIndex>
                    <LookBackPeriod>0</LookBackPeriod>
                    <MarketPosition>Long</MarketPosition>
                    <Period>0</Period>
                    <ReturnType>Number</ReturnType>
                    <StartBar>0</StartBar>
                    <State>Undefined</State>
                    <Time>0001-01-01T00:00:00</Time>
                  </Action>
                  <Index>2</Index>
                  <StringValue>CurrentBars[0]</StringValue>
                </NinjaScriptString>
              </Strings>
            </Tag>
            <Text>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Action>
                    <IsExpanded>false</IsExpanded>
                    <IsSelected>true</IsSelected>
                    <Name>GapSize</Name>
                    <OffsetType>Arithmetic</OffsetType>
                    <AssignedCommand>
                      <Command>GapSize</Command>
                      <Parameters />
                    </AssignedCommand>
                    <BarsAgo>0</BarsAgo>
                    <CurrencyType>Currency</CurrencyType>
                    <Date>2025-05-31T17:58:40.960575</Date>
                    <DayOfWeek>Sunday</DayOfWeek>
                    <EndBar>0</EndBar>
                    <ForceSeriesIndex>false</ForceSeriesIndex>
                    <LookBackPeriod>0</LookBackPeriod>
                    <MarketPosition>Long</MarketPosition>
                    <Period>0</Period>
                    <ReturnType>Number</ReturnType>
                    <StartBar>0</StartBar>
                    <State>Undefined</State>
                    <Time>0001-01-01T00:00:00</Time>
                  </Action>
                  <Index>0</Index>
                  <StringValue>GapSize</StringValue>
                </NinjaScriptString>
              </Strings>
            </Text>
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2025-05-31T21:05:31.7995009</VariableDateTime>
            <VariableBool>false</VariableBool>
            <Y>
              <DefaultValue>0</DefaultValue>
              <IsInt>false</IsInt>
              <BindingValue xsi:type="xsd:string">(High[2] + 100) </BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>High</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>2</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:46:47.4272241</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Add</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">100</BindingValue>
                    <IsLiteral>true</IsLiteral>
                    <LiveValue xsi:type="xsd:string">100</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>High</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">(High[2] + 100) </LiveValue>
            </Y>
          </ActionProperties>
          <ActionType>Drawing</ActionType>
          <Command>
            <Command>Text</Command>
            <Parameters>
              <string>owner</string>
              <string>tag</string>
              <string>text</string>
              <string>barsAgo</string>
              <string>y</string>
            </Parameters>
          </Command>
        </WizardAction>
      </Actions>
      <ActiveAction>
        <IsExpanded>false</IsExpanded>
        <IsSelected>true</IsSelected>
        <Name>Text</Name>
        <OffsetType>Arithmetic</OffsetType>
        <ActionProperties>
          <DashStyle>Solid</DashStyle>
          <DivideTimePrice>false</DivideTimePrice>
          <Id />
          <File />
          <IsAutoScale>false</IsAutoScale>
          <IsSimulatedStop>false</IsSimulatedStop>
          <IsStop>false</IsStop>
          <LogLevel>Information</LogLevel>
          <Mode>Currency</Mode>
          <OffsetType>Currency</OffsetType>
          <Priority>Medium</Priority>
          <Quantity>
            <DefaultValue>0</DefaultValue>
            <IsInt>true</IsInt>
            <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
            <DynamicValue>
              <IsExpanded>false</IsExpanded>
              <IsSelected>false</IsSelected>
              <Name>Default order quantity</Name>
              <OffsetType>Arithmetic</OffsetType>
              <AssignedCommand>
                <Command>DefaultQuantity</Command>
                <Parameters />
              </AssignedCommand>
              <BarsAgo>0</BarsAgo>
              <CurrencyType>Currency</CurrencyType>
              <Date>2025-05-31T21:05:31.7995009</Date>
              <DayOfWeek>Sunday</DayOfWeek>
              <EndBar>0</EndBar>
              <ForceSeriesIndex>false</ForceSeriesIndex>
              <LookBackPeriod>0</LookBackPeriod>
              <MarketPosition>Long</MarketPosition>
              <Period>0</Period>
              <ReturnType>Number</ReturnType>
              <StartBar>0</StartBar>
              <State>Undefined</State>
              <Time>0001-01-01T00:00:00</Time>
            </DynamicValue>
            <IsLiteral>false</IsLiteral>
            <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
          </Quantity>
          <ServiceName />
          <ScreenshotPath />
          <SoundLocation />
          <Tag>
            <SeparatorCharacter> </SeparatorCharacter>
            <Strings>
              <NinjaScriptString>
                <Index>0</Index>
                <StringValue>FVW</StringValue>
              </NinjaScriptString>
              <NinjaScriptString>
                <Index>1</Index>
                <StringValue>Text_1</StringValue>
              </NinjaScriptString>
              <NinjaScriptString>
                <Action>
                  <Children />
                  <IsExpanded>false</IsExpanded>
                  <IsSelected>true</IsSelected>
                  <Name>Current bar</Name>
                  <OffsetType>Arithmetic</OffsetType>
                  <AssignedCommand>
                    <Command>CurrentBars[{0}]</Command>
                    <Parameters>
                      <string>Series1</string>
                    </Parameters>
                  </AssignedCommand>
                  <BarsAgo>0</BarsAgo>
                  <CurrencyType>Currency</CurrencyType>
                  <Date>2025-05-31T17:44:48.6414848</Date>
                  <DayOfWeek>Sunday</DayOfWeek>
                  <EndBar>0</EndBar>
                  <ForceSeriesIndex>true</ForceSeriesIndex>
                  <LookBackPeriod>0</LookBackPeriod>
                  <MarketPosition>Long</MarketPosition>
                  <Period>0</Period>
                  <ReturnType>Number</ReturnType>
                  <StartBar>0</StartBar>
                  <State>Undefined</State>
                  <Time>0001-01-01T00:00:00</Time>
                </Action>
                <Index>2</Index>
                <StringValue>CurrentBars[0]</StringValue>
              </NinjaScriptString>
            </Strings>
          </Tag>
          <Text>
            <SeparatorCharacter> </SeparatorCharacter>
            <Strings>
              <NinjaScriptString>
                <Action>
                  <IsExpanded>false</IsExpanded>
                  <IsSelected>true</IsSelected>
                  <Name>GapSize</Name>
                  <OffsetType>Arithmetic</OffsetType>
                  <AssignedCommand>
                    <Command>GapSize</Command>
                    <Parameters />
                  </AssignedCommand>
                  <BarsAgo>0</BarsAgo>
                  <CurrencyType>Currency</CurrencyType>
                  <Date>2025-05-31T17:58:40.960575</Date>
                  <DayOfWeek>Sunday</DayOfWeek>
                  <EndBar>0</EndBar>
                  <ForceSeriesIndex>false</ForceSeriesIndex>
                  <LookBackPeriod>0</LookBackPeriod>
                  <MarketPosition>Long</MarketPosition>
                  <Period>0</Period>
                  <ReturnType>Number</ReturnType>
                  <StartBar>0</StartBar>
                  <State>Undefined</State>
                  <Time>0001-01-01T00:00:00</Time>
                </Action>
                <Index>0</Index>
                <StringValue>GapSize</StringValue>
              </NinjaScriptString>
            </Strings>
          </Text>
          <TextPosition>BottomLeft</TextPosition>
          <VariableDateTime>2025-05-31T21:05:31.7995009</VariableDateTime>
          <VariableBool>false</VariableBool>
          <Y>
            <DefaultValue>0</DefaultValue>
            <IsInt>false</IsInt>
            <BindingValue xsi:type="xsd:string">(High[2] + 100) </BindingValue>
            <DynamicValue>
              <Children />
              <IsExpanded>false</IsExpanded>
              <IsSelected>true</IsSelected>
              <Name>High</Name>
              <OffsetType>Arithmetic</OffsetType>
              <AssignedCommand>
                <Command>{0}</Command>
                <Parameters>
                  <string>Series1</string>
                  <string>BarsAgo</string>
                  <string>OffsetBuilder</string>
                </Parameters>
              </AssignedCommand>
              <BarsAgo>2</BarsAgo>
              <CurrencyType>Currency</CurrencyType>
              <Date>2025-05-31T17:46:47.4272241</Date>
              <DayOfWeek>Sunday</DayOfWeek>
              <EndBar>0</EndBar>
              <ForceSeriesIndex>false</ForceSeriesIndex>
              <LookBackPeriod>0</LookBackPeriod>
              <MarketPosition>Long</MarketPosition>
              <OffsetBuilder>
                <ConditionOffset>
                  <IsSetEnabled>false</IsSetEnabled>
                  <OffsetValue>0</OffsetValue>
                  <OffsetOperator>Add</OffsetOperator>
                  <OffsetType>Arithmetic</OffsetType>
                </ConditionOffset>
                <Offset>
                  <DefaultValue>0</DefaultValue>
                  <IsInt>false</IsInt>
                  <BindingValue xsi:type="xsd:string">100</BindingValue>
                  <IsLiteral>true</IsLiteral>
                  <LiveValue xsi:type="xsd:string">100</LiveValue>
                </Offset>
              </OffsetBuilder>
              <Period>0</Period>
              <ReturnType>Series</ReturnType>
              <Series1>
                <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                <CustomProperties />
                <IsExplicitlyNamed>false</IsExplicitlyNamed>
                <IsPriceTypeLocked>true</IsPriceTypeLocked>
                <PlotOnChart>false</PlotOnChart>
                <PriceType>High</PriceType>
                <SeriesType>DefaultSeries</SeriesType>
              </Series1>
              <StartBar>0</StartBar>
              <State>Undefined</State>
              <Time>0001-01-01T00:00:00</Time>
            </DynamicValue>
            <IsLiteral>false</IsLiteral>
            <LiveValue xsi:type="xsd:string">(High[2] + 100) </LiveValue>
          </Y>
        </ActionProperties>
        <ActionType>Drawing</ActionType>
        <Command>
          <Command>Text</Command>
          <Parameters>
            <string>owner</string>
            <string>tag</string>
            <string>text</string>
            <string>barsAgo</string>
            <string>y</string>
          </Parameters>
        </Command>
      </ActiveAction>
      <AnyOrAll>All</AnyOrAll>
      <Conditions>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Ask</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>GetCurrentAsk({0})</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:24:49.4136177</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>true</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Add</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">2</BindingValue>
                    <IsLiteral>true</IsLiteral>
                    <LiveValue xsi:type="xsd:string">2</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Less</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>VWAPx</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>VWAP</Command>
                  <Parameters>
                    <string>AssociatedIndicator</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <AssociatedIndicator>
                  <AcceptableSeries>Indicator DataSeries CustomSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IndicatorHolder>
                    <IndicatorName>VWAP</IndicatorName>
                    <Plots>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#FF00FFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Dot</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Dot</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP1U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Dot</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP1L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP2U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP2L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP3U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP3L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP4U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP4L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP5U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP5L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                    </Plots>
                  </IndicatorHolder>
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>false</IsPriceTypeLocked>
                  <PlotOnChart>true</PlotOnChart>
                  <PriceType>Close</PriceType>
                  <SeriesType>Indicator</SeriesType>
                  <SelectedPlot>PlotVWAP</SelectedPlot>
                </AssociatedIndicator>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:24:49.4376488</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>(GetCurrentAsk(Default input) + 2)  &lt; VWAP().PlotVWAP[0]</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>CanTrade</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>CanTrade</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:33:14.7713401</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Bool</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Equals</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>True</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>true</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:33:14.7837101</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Bool</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>CanTrade = true</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Low</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>3</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:57:49.9337361</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Subtract</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">High[1]</BindingValue>
                    <DynamicValue>
                      <IsExpanded>false</IsExpanded>
                      <IsSelected>true</IsSelected>
                      <Name>High</Name>
                      <OffsetType>Arithmetic</OffsetType>
                      <AssignedCommand>
                        <Command>{0}</Command>
                        <Parameters>
                          <string>Series1</string>
                          <string>BarsAgo</string>
                          <string>OffsetBuilder</string>
                        </Parameters>
                      </AssignedCommand>
                      <BarsAgo>1</BarsAgo>
                      <CurrencyType>Currency</CurrencyType>
                      <Date>2025-05-31T17:57:55.9680276</Date>
                      <DayOfWeek>Sunday</DayOfWeek>
                      <EndBar>0</EndBar>
                      <ForceSeriesIndex>false</ForceSeriesIndex>
                      <LookBackPeriod>0</LookBackPeriod>
                      <MarketPosition>Long</MarketPosition>
                      <Period>0</Period>
                      <ReturnType>Series</ReturnType>
                      <Series1>
                        <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                        <CustomProperties />
                        <IsExplicitlyNamed>false</IsExplicitlyNamed>
                        <IsPriceTypeLocked>true</IsPriceTypeLocked>
                        <PlotOnChart>false</PlotOnChart>
                        <PriceType>High</PriceType>
                        <SeriesType>DefaultSeries</SeriesType>
                      </Series1>
                      <StartBar>0</StartBar>
                      <State>Undefined</State>
                      <Time>0001-01-01T00:00:00</Time>
                    </DynamicValue>
                    <IsLiteral>false</IsLiteral>
                    <LiveValue xsi:type="xsd:string">High[1]</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>Low</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>GreaterEqual</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>SizeFVG</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>SizeFVG</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:43:05.9372632</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Custom FVG B</DisplayName>
        </WizardConditionGroup>
      </Conditions>
      <SetName>Set 3</SetName>
      <SetNumber>3</SetNumber>
    </ConditionalAction>
    <ConditionalAction>
      <Actions>
        <WizardAction>
          <Children />
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Arrow up</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <BarsAgo>2</BarsAgo>
            <Brush xsi:type="SolidColorBrush">
              <Opacity>1</Opacity>
              <Transform xsi:type="MatrixTransform">
                <Matrix>
                  <M11>1</M11>
                  <M12>0</M12>
                  <M21>0</M21>
                  <M22>1</M22>
                  <OffsetX>0</OffsetX>
                  <OffsetY>0</OffsetY>
                </Matrix>
              </Transform>
              <RelativeTransform xsi:type="MatrixTransform">
                <Matrix>
                  <M11>1</M11>
                  <M12>0</M12>
                  <M21>0</M21>
                  <M22>1</M22>
                  <OffsetX>0</OffsetX>
                  <OffsetY>0</OffsetY>
                </Matrix>
              </RelativeTransform>
              <Color>
                <A>255</A>
                <R>0</R>
                <G>255</G>
                <B>0</B>
                <ScA>1</ScA>
                <ScR>0</ScR>
                <ScG>1</ScG>
                <ScB>0</ScB>
              </Color>
            </Brush>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:22:14.367516</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>FVW</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Index>1</Index>
                  <StringValue>Arrow up_1</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Action>
                    <Children />
                    <IsExpanded>false</IsExpanded>
                    <IsSelected>true</IsSelected>
                    <Name>Current bar</Name>
                    <OffsetType>Arithmetic</OffsetType>
                    <AssignedCommand>
                      <Command>CurrentBars[{0}]</Command>
                      <Parameters>
                        <string>Series1</string>
                      </Parameters>
                    </AssignedCommand>
                    <BarsAgo>0</BarsAgo>
                    <CurrencyType>Currency</CurrencyType>
                    <Date>2025-05-31T02:22:26.6947953</Date>
                    <DayOfWeek>Sunday</DayOfWeek>
                    <EndBar>0</EndBar>
                    <ForceSeriesIndex>true</ForceSeriesIndex>
                    <LookBackPeriod>0</LookBackPeriod>
                    <MarketPosition>Long</MarketPosition>
                    <Period>0</Period>
                    <ReturnType>Number</ReturnType>
                    <StartBar>0</StartBar>
                    <State>Undefined</State>
                    <Time>0001-01-01T00:00:00</Time>
                  </Action>
                  <Index>2</Index>
                  <StringValue>CurrentBars[0]</StringValue>
                </NinjaScriptString>
              </Strings>
            </Tag>
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2025-05-31T02:22:14.367516</VariableDateTime>
            <VariableBool>false</VariableBool>
            <Y>
              <DefaultValue>0</DefaultValue>
              <IsInt>false</IsInt>
              <BindingValue xsi:type="xsd:string">Low[2]</BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Low</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>2</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:22:38.246043</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>Low</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">Low[2]</LiveValue>
            </Y>
          </ActionProperties>
          <ActionType>Drawing</ActionType>
          <Command>
            <Command>ArrowUp</Command>
            <Parameters>
              <string>owner</string>
              <string>tag</string>
              <string>isAutoScale</string>
              <string>barsAgo</string>
              <string>y</string>
              <string>brush</string>
            </Parameters>
          </Command>
        </WizardAction>
        <WizardAction>
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Enter long position</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T21:03:09.7999869</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SignalName>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>LONG</StringValue>
                </NinjaScriptString>
              </Strings>
            </SignalName>
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>Set Enter long position</StringValue>
                </NinjaScriptString>
              </Strings>
            </Tag>
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2025-05-31T21:03:09.7999869</VariableDateTime>
            <VariableBool>false</VariableBool>
          </ActionProperties>
          <ActionType>Enter</ActionType>
          <Command>
            <Command>EnterLong</Command>
            <Parameters>
              <string>quantity</string>
              <string>signalName</string>
            </Parameters>
          </Command>
        </WizardAction>
        <WizardAction>
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Set GapSize</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T21:05:59.4645192</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <TextPosition>BottomLeft</TextPosition>
            <VariableInt>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">(Low[1] - (High[3])) </BindingValue>
              <DynamicValue>
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Low</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>1</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:57:13.742561</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Subtract</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">High[3]</BindingValue>
                    <DynamicValue>
                      <Children />
                      <IsExpanded>false</IsExpanded>
                      <IsSelected>true</IsSelected>
                      <Name>High</Name>
                      <OffsetType>Arithmetic</OffsetType>
                      <AssignedCommand>
                        <Command>{0}</Command>
                        <Parameters>
                          <string>Series1</string>
                          <string>BarsAgo</string>
                          <string>OffsetBuilder</string>
                        </Parameters>
                      </AssignedCommand>
                      <BarsAgo>3</BarsAgo>
                      <CurrencyType>Currency</CurrencyType>
                      <Date>2025-05-31T17:57:22.2030938</Date>
                      <DayOfWeek>Sunday</DayOfWeek>
                      <EndBar>0</EndBar>
                      <ForceSeriesIndex>false</ForceSeriesIndex>
                      <LookBackPeriod>0</LookBackPeriod>
                      <MarketPosition>Long</MarketPosition>
                      <Period>0</Period>
                      <ReturnType>Series</ReturnType>
                      <Series1>
                        <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                        <CustomProperties />
                        <IsExplicitlyNamed>false</IsExplicitlyNamed>
                        <IsPriceTypeLocked>true</IsPriceTypeLocked>
                        <PlotOnChart>false</PlotOnChart>
                        <PriceType>High</PriceType>
                        <SeriesType>DefaultSeries</SeriesType>
                      </Series1>
                      <StartBar>0</StartBar>
                      <State>Undefined</State>
                      <Time>0001-01-01T00:00:00</Time>
                    </DynamicValue>
                    <IsLiteral>false</IsLiteral>
                    <LiveValue xsi:type="xsd:string">High[3]</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>Low</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">(Low[1] - (High[3])) </LiveValue>
            </VariableInt>
            <VariableDateTime>2025-05-31T21:05:59.4645192</VariableDateTime>
            <VariableBool>false</VariableBool>
          </ActionProperties>
          <ActionType>SetValue</ActionType>
          <UserVariableType>int</UserVariableType>
          <VariableName>GapSize</VariableName>
        </WizardAction>
        <WizardAction>
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Text</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <DashStyle>Solid</DashStyle>
            <DivideTimePrice>false</DivideTimePrice>
            <Id />
            <File />
            <IsAutoScale>false</IsAutoScale>
            <IsSimulatedStop>false</IsSimulatedStop>
            <IsStop>false</IsStop>
            <LogLevel>Information</LogLevel>
            <Mode>Currency</Mode>
            <OffsetType>Currency</OffsetType>
            <Priority>Medium</Priority>
            <Quantity>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DynamicValue>
                <IsExpanded>false</IsExpanded>
                <IsSelected>false</IsSelected>
                <Name>Default order quantity</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>DefaultQuantity</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T21:05:06.7601658</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>FVW</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Index>1</Index>
                  <StringValue>Text_1</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Action>
                    <Children />
                    <IsExpanded>false</IsExpanded>
                    <IsSelected>true</IsSelected>
                    <Name>Current bar</Name>
                    <OffsetType>Arithmetic</OffsetType>
                    <AssignedCommand>
                      <Command>CurrentBars[{0}]</Command>
                      <Parameters>
                        <string>Series1</string>
                      </Parameters>
                    </AssignedCommand>
                    <BarsAgo>0</BarsAgo>
                    <CurrencyType>Currency</CurrencyType>
                    <Date>2025-05-31T17:44:48.6414848</Date>
                    <DayOfWeek>Sunday</DayOfWeek>
                    <EndBar>0</EndBar>
                    <ForceSeriesIndex>true</ForceSeriesIndex>
                    <LookBackPeriod>0</LookBackPeriod>
                    <MarketPosition>Long</MarketPosition>
                    <Period>0</Period>
                    <ReturnType>Number</ReturnType>
                    <StartBar>0</StartBar>
                    <State>Undefined</State>
                    <Time>0001-01-01T00:00:00</Time>
                  </Action>
                  <Index>2</Index>
                  <StringValue>CurrentBars[0]</StringValue>
                </NinjaScriptString>
              </Strings>
            </Tag>
            <Text>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Action>
                    <Children />
                    <IsExpanded>false</IsExpanded>
                    <IsSelected>true</IsSelected>
                    <Name>GapSize</Name>
                    <OffsetType>Arithmetic</OffsetType>
                    <AssignedCommand>
                      <Command>GapSize</Command>
                      <Parameters />
                    </AssignedCommand>
                    <BarsAgo>0</BarsAgo>
                    <CurrencyType>Currency</CurrencyType>
                    <Date>2025-05-31T17:58:40.960575</Date>
                    <DayOfWeek>Sunday</DayOfWeek>
                    <EndBar>0</EndBar>
                    <ForceSeriesIndex>false</ForceSeriesIndex>
                    <LookBackPeriod>0</LookBackPeriod>
                    <MarketPosition>Long</MarketPosition>
                    <Period>0</Period>
                    <ReturnType>Number</ReturnType>
                    <StartBar>0</StartBar>
                    <State>Undefined</State>
                    <Time>0001-01-01T00:00:00</Time>
                  </Action>
                  <Index>0</Index>
                  <StringValue>GapSize</StringValue>
                </NinjaScriptString>
              </Strings>
            </Text>
            <TextPosition>BottomLeft</TextPosition>
            <VariableDateTime>2025-05-31T21:05:06.7601658</VariableDateTime>
            <VariableBool>false</VariableBool>
            <Y>
              <DefaultValue>0</DefaultValue>
              <IsInt>false</IsInt>
              <BindingValue xsi:type="xsd:string">(Low[2] - 100) </BindingValue>
              <DynamicValue>
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Low</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>2</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T21:05:17.9626011</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Subtract</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">100</BindingValue>
                    <IsLiteral>true</IsLiteral>
                    <LiveValue xsi:type="xsd:string">100</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>Low</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </DynamicValue>
              <IsLiteral>false</IsLiteral>
              <LiveValue xsi:type="xsd:string">(Low[2] - 100) </LiveValue>
            </Y>
          </ActionProperties>
          <ActionType>Drawing</ActionType>
          <Command>
            <Command>Text</Command>
            <Parameters>
              <string>owner</string>
              <string>tag</string>
              <string>text</string>
              <string>barsAgo</string>
              <string>y</string>
            </Parameters>
          </Command>
        </WizardAction>
      </Actions>
      <AnyOrAll>All</AnyOrAll>
      <Conditions>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Ask</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>GetCurrentAsk({0})</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:24:49.4136177</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>true</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Add</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">2</BindingValue>
                    <IsLiteral>true</IsLiteral>
                    <LiveValue xsi:type="xsd:string">2</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Greater</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>VWAPx</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>VWAP</Command>
                  <Parameters>
                    <string>AssociatedIndicator</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <AssociatedIndicator>
                  <AcceptableSeries>Indicator DataSeries CustomSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IndicatorHolder>
                    <IndicatorName>VWAP</IndicatorName>
                    <Plots>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#FF00FFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Dot</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Dot</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP1U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Dot</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP1L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP2U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP2L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP3U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP3L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP4U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP4L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP5U</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                      <Plot>
                        <IsOpacityVisible>false</IsOpacityVisible>
                        <BrushSerialize>&lt;SolidColorBrush xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"&gt;#00FFFFFF&lt;/SolidColorBrush&gt;</BrushSerialize>
                        <DashStyleHelper>Solid</DashStyleHelper>
                        <Opacity>100</Opacity>
                        <Width>1</Width>
                        <AutoWidth>false</AutoWidth>
                        <Max>1.7976931348623157E+308</Max>
                        <Min>-1.7976931348623157E+308</Min>
                        <Name>PlotVWAP5L</Name>
                        <PlotStyle>Line</PlotStyle>
                      </Plot>
                    </Plots>
                  </IndicatorHolder>
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>false</IsPriceTypeLocked>
                  <PlotOnChart>true</PlotOnChart>
                  <PriceType>Close</PriceType>
                  <SeriesType>Indicator</SeriesType>
                  <SelectedPlot>PlotVWAP</SelectedPlot>
                </AssociatedIndicator>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:24:49.4376488</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>(GetCurrentAsk(Default input) + 2)  &gt; VWAP().PlotVWAP[0]</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>Any</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>CanTrade</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>CanTrade</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:33:14.7713401</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Bool</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Equals</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>True</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>true</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T02:33:14.7837101</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Bool</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>false</IsGroup>
          <DisplayName>CanTrade = true</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Low</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>{0}</Command>
                  <Parameters>
                    <string>Series1</string>
                    <string>BarsAgo</string>
                    <string>OffsetBuilder</string>
                  </Parameters>
                </AssignedCommand>
                <BarsAgo>1</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:57:49.9337361</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                    <OffsetOperator>Subtract</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                  </ConditionOffset>
                  <Offset>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <BindingValue xsi:type="xsd:string">High[3]</BindingValue>
                    <DynamicValue>
                      <Children />
                      <IsExpanded>false</IsExpanded>
                      <IsSelected>true</IsSelected>
                      <Name>High</Name>
                      <OffsetType>Arithmetic</OffsetType>
                      <AssignedCommand>
                        <Command>{0}</Command>
                        <Parameters>
                          <string>Series1</string>
                          <string>BarsAgo</string>
                          <string>OffsetBuilder</string>
                        </Parameters>
                      </AssignedCommand>
                      <BarsAgo>3</BarsAgo>
                      <CurrencyType>Currency</CurrencyType>
                      <Date>2025-05-31T17:57:55.9680276</Date>
                      <DayOfWeek>Sunday</DayOfWeek>
                      <EndBar>0</EndBar>
                      <ForceSeriesIndex>false</ForceSeriesIndex>
                      <LookBackPeriod>0</LookBackPeriod>
                      <MarketPosition>Long</MarketPosition>
                      <Period>0</Period>
                      <ReturnType>Series</ReturnType>
                      <Series1>
                        <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                        <CustomProperties />
                        <IsExplicitlyNamed>false</IsExplicitlyNamed>
                        <IsPriceTypeLocked>true</IsPriceTypeLocked>
                        <PlotOnChart>false</PlotOnChart>
                        <PriceType>High</PriceType>
                        <SeriesType>DefaultSeries</SeriesType>
                      </Series1>
                      <StartBar>0</StartBar>
                      <State>Undefined</State>
                      <Time>0001-01-01T00:00:00</Time>
                    </DynamicValue>
                    <IsLiteral>false</IsLiteral>
                    <LiveValue xsi:type="xsd:string">High[3]</LiveValue>
                  </Offset>
                </OffsetBuilder>
                <Period>0</Period>
                <ReturnType>Series</ReturnType>
                <Series1>
                  <AcceptableSeries>DataSeries DefaultSeries</AcceptableSeries>
                  <CustomProperties />
                  <IsExplicitlyNamed>false</IsExplicitlyNamed>
                  <IsPriceTypeLocked>true</IsPriceTypeLocked>
                  <PlotOnChart>false</PlotOnChart>
                  <PriceType>Low</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>GreaterEqual</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>SizeFVG</Name>
                <OffsetType>Arithmetic</OffsetType>
                <AssignedCommand>
                  <Command>SizeFVG</Command>
                  <Parameters />
                </AssignedCommand>
                <BarsAgo>0</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-05-31T17:43:05.9372632</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <Period>0</Period>
                <ReturnType>Number</ReturnType>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Custom FVG A</DisplayName>
        </WizardConditionGroup>
      </Conditions>
      <SetName>Set 4</SetName>
      <SetNumber>4</SetNumber>
    </ConditionalAction>
  </ConditionalActions>
  <CustomSeries>
    <CustomSeriesProperties>
      <Name>StopSizeSerie</Name>
      <Type>Double</Type>
      <SeriesID>bf374552-3eef-49e6-8604-dc2d26826a78</SeriesID>
    </CustomSeriesProperties>
  </CustomSeries>
  <DataSeries />
  <Description>Aprovechamos los FVG con direccion del vwap.</Description>
  <DisplayInDataBox>true</DisplayInDataBox>
  <DrawHorizontalGridLines>true</DrawHorizontalGridLines>
  <DrawOnPricePanel>true</DrawOnPricePanel>
  <DrawVerticalGridLines>true</DrawVerticalGridLines>
  <EntriesPerDirection>1</EntriesPerDirection>
  <EntryHandling>AllEntries</EntryHandling>
  <ExitOnSessionClose>true</ExitOnSessionClose>
  <ExitOnSessionCloseSeconds>30</ExitOnSessionCloseSeconds>
  <FillLimitOrdersOnTouch>false</FillLimitOrdersOnTouch>
  <InputParameters>
    <InputParameter>
      <Default>15:30</Default>
      <Description />
      <Name>STime</Name>
      <Minimum />
      <Type>time</Type>
    </InputParameter>
    <InputParameter>
      <Default>17:30</Default>
      <Description />
      <Name>Etime</Name>
      <Minimum />
      <Type>time</Type>
    </InputParameter>
    <InputParameter>
      <Default>100</Default>
      <Description />
      <Name>StopLoss</Name>
      <Minimum>1</Minimum>
      <Type>int</Type>
    </InputParameter>
    <InputParameter>
      <Default>100</Default>
      <Description />
      <Name>TakeProfit</Name>
      <Minimum>1</Minimum>
      <Type>int</Type>
    </InputParameter>
    <InputParameter>
      <Default>10</Default>
      <Description />
      <Name>SizeFVG</Name>
      <Minimum>1</Minimum>
      <Type>int</Type>
    </InputParameter>
    <InputParameter>
      <Default>25</Default>
      <Description>Porciento de la operacion para activar el trailing.</Description>
      <Name>TrailingTrigger</Name>
      <Minimum>1</Minimum>
      <Type>int</Type>
    </InputParameter>
  </InputParameters>
  <IsTradingHoursBreakLineVisible>true</IsTradingHoursBreakLineVisible>
  <IsInstantiatedOnEachOptimizationIteration>true</IsInstantiatedOnEachOptimizationIteration>
  <MaximumBarsLookBack>TwoHundredFiftySix</MaximumBarsLookBack>
  <MinimumBarsRequired>20</MinimumBarsRequired>
  <OrderFillResolution>Standard</OrderFillResolution>
  <OrderFillResolutionValue>1</OrderFillResolutionValue>
  <OrderFillResolutionType>Minute</OrderFillResolutionType>
  <OverlayOnPrice>false</OverlayOnPrice>
  <PaintPriceMarkers>true</PaintPriceMarkers>
  <PlotParameters />
  <RealTimeErrorHandling>StopCancelClose</RealTimeErrorHandling>
  <ScaleJustification>Right</ScaleJustification>
  <ScriptType>Strategy</ScriptType>
  <Slippage>0</Slippage>
  <StartBehavior>WaitUntilFlat</StartBehavior>
  <StopsAndTargets>
    <WizardAction>
      <Children />
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Profit target</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <FromEntrySignal>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>SHORT</StringValue>
            </NinjaScriptString>
          </Strings>
        </FromEntrySignal>
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>false</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Ticks</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:31:05.7346211</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">TakeProfit</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>TakeProfit</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>TakeProfit</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:31:09.6658049</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">TakeProfit</LiveValue>
        </Value>
        <VariableDateTime>2025-05-31T02:31:05.7346211</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetProfitTarget</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
        </Parameters>
      </Command>
    </WizardAction>
    <WizardAction>
      <Children />
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Profit target</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <FromEntrySignal>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>LONG</StringValue>
            </NinjaScriptString>
          </Strings>
        </FromEntrySignal>
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>false</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Ticks</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:30:59.7838968</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">TakeProfit</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>TakeProfit</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>TakeProfit</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:31:01.3426983</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">TakeProfit</LiveValue>
        </Value>
        <VariableDateTime>2025-05-31T02:30:59.7838968</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetProfitTarget</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
        </Parameters>
      </Command>
    </WizardAction>
    <WizardAction>
      <Children />
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Stop loss</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <FromEntrySignal>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>SHORT</StringValue>
            </NinjaScriptString>
          </Strings>
        </FromEntrySignal>
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>false</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Ticks</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:30:23.1030978</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <Tag>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>Set Stop loss</StringValue>
            </NinjaScriptString>
          </Strings>
        </Tag>
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">StopLoss</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>StopLoss</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>StopLoss</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:30:34.5010908</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">StopLoss</LiveValue>
        </Value>
        <VariableDateTime>2025-05-31T02:30:23.1030978</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetStopLoss</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
          <string>isSimulatedStop</string>
        </Parameters>
      </Command>
    </WizardAction>
    <WizardAction>
      <Children />
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Stop loss</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <FromEntrySignal>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>LONG</StringValue>
            </NinjaScriptString>
          </Strings>
        </FromEntrySignal>
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>false</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Currency</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:30:41.3430509</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <Tag>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>Set Stop loss</StringValue>
            </NinjaScriptString>
          </Strings>
        </Tag>
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">StopLoss</BindingValue>
          <DynamicValue>
            <Children />
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>StopLoss</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>StopLoss</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T02:30:47.9484234</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">StopLoss</LiveValue>
        </Value>
        <VariableDateTime>2025-05-31T02:30:41.3430509</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetStopLoss</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
          <string>isSimulatedStop</string>
        </Parameters>
      </Command>
    </WizardAction>
    <WizardAction>
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Trailing stop</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <FromEntrySignal>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>LONG</StringValue>
            </NinjaScriptString>
          </Strings>
        </FromEntrySignal>
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>false</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Percent</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T22:44:39.1167737</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">TrailingTrigger</BindingValue>
          <DynamicValue>
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>TrailingTrigger</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>TrailingTrigger</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T22:44:41.4291179</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">TrailingTrigger</LiveValue>
        </Value>
        <VariableDateTime>2025-05-31T22:44:39.1167737</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetTrailStop</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
          <string>isSimulatedStop</string>
        </Parameters>
      </Command>
    </WizardAction>
    <WizardAction>
      <IsExpanded>false</IsExpanded>
      <IsSelected>true</IsSelected>
      <Name>Trailing stop</Name>
      <OffsetType>Arithmetic</OffsetType>
      <ActionProperties>
        <DashStyle>Solid</DashStyle>
        <DivideTimePrice>false</DivideTimePrice>
        <Id />
        <File />
        <FromEntrySignal>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>SHORT</StringValue>
            </NinjaScriptString>
          </Strings>
        </FromEntrySignal>
        <IsAutoScale>false</IsAutoScale>
        <IsSimulatedStop>false</IsSimulatedStop>
        <IsStop>false</IsStop>
        <LogLevel>Information</LogLevel>
        <Mode>Percent</Mode>
        <OffsetType>Currency</OffsetType>
        <Priority>Medium</Priority>
        <Quantity>
          <DefaultValue>0</DefaultValue>
          <IsInt>true</IsInt>
          <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
          <DynamicValue>
            <IsExpanded>false</IsExpanded>
            <IsSelected>false</IsSelected>
            <Name>Default order quantity</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>DefaultQuantity</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T22:44:49.0959294</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
        </Quantity>
        <ServiceName />
        <ScreenshotPath />
        <SoundLocation />
        <Tag>
          <SeparatorCharacter> </SeparatorCharacter>
          <Strings>
            <NinjaScriptString>
              <Index>0</Index>
              <StringValue>Set Trailing stop</StringValue>
            </NinjaScriptString>
          </Strings>
        </Tag>
        <TextPosition>BottomLeft</TextPosition>
        <Value>
          <DefaultValue>0</DefaultValue>
          <IsInt>false</IsInt>
          <BindingValue xsi:type="xsd:string">TrailingTrigger</BindingValue>
          <DynamicValue>
            <IsExpanded>false</IsExpanded>
            <IsSelected>true</IsSelected>
            <Name>TrailingTrigger</Name>
            <OffsetType>Arithmetic</OffsetType>
            <AssignedCommand>
              <Command>TrailingTrigger</Command>
              <Parameters />
            </AssignedCommand>
            <BarsAgo>0</BarsAgo>
            <CurrencyType>Currency</CurrencyType>
            <Date>2025-05-31T22:45:03.4456744</Date>
            <DayOfWeek>Sunday</DayOfWeek>
            <EndBar>0</EndBar>
            <ForceSeriesIndex>false</ForceSeriesIndex>
            <LookBackPeriod>0</LookBackPeriod>
            <MarketPosition>Long</MarketPosition>
            <Period>0</Period>
            <ReturnType>Number</ReturnType>
            <StartBar>0</StartBar>
            <State>Undefined</State>
            <Time>0001-01-01T00:00:00</Time>
          </DynamicValue>
          <IsLiteral>false</IsLiteral>
          <LiveValue xsi:type="xsd:string">TrailingTrigger</LiveValue>
        </Value>
        <VariableDateTime>2025-05-31T22:44:49.0959294</VariableDateTime>
        <VariableBool>false</VariableBool>
      </ActionProperties>
      <ActionType>Misc</ActionType>
      <Command>
        <Command>SetTrailStop</Command>
        <Parameters>
          <string>fromEntrySignal</string>
          <string>mode</string>
          <string>value</string>
          <string>isSimulatedStop</string>
        </Parameters>
      </Command>
    </WizardAction>
  </StopsAndTargets>
  <StopTargetHandling>PerEntryExecution</StopTargetHandling>
  <TimeInForce>Gtc</TimeInForce>
  <TraceOrders>false</TraceOrders>
  <UseOnAddTradeEvent>false</UseOnAddTradeEvent>
  <UseOnAuthorizeAccountEvent>false</UseOnAuthorizeAccountEvent>
  <UseAccountItemUpdate>false</UseAccountItemUpdate>
  <UseOnCalculatePerformanceValuesEvent>true</UseOnCalculatePerformanceValuesEvent>
  <UseOnConnectionEvent>false</UseOnConnectionEvent>
  <UseOnDataPointEvent>true</UseOnDataPointEvent>
  <UseOnFundamentalDataEvent>false</UseOnFundamentalDataEvent>
  <UseOnExecutionEvent>false</UseOnExecutionEvent>
  <UseOnMouseDown>true</UseOnMouseDown>
  <UseOnMouseMove>true</UseOnMouseMove>
  <UseOnMouseUp>true</UseOnMouseUp>
  <UseOnMarketDataEvent>false</UseOnMarketDataEvent>
  <UseOnMarketDepthEvent>false</UseOnMarketDepthEvent>
  <UseOnMergePerformanceMetricEvent>false</UseOnMergePerformanceMetricEvent>
  <UseOnNextDataPointEvent>true</UseOnNextDataPointEvent>
  <UseOnNextInstrumentEvent>true</UseOnNextInstrumentEvent>
  <UseOnOptimizeEvent>true</UseOnOptimizeEvent>
  <UseOnOrderUpdateEvent>false</UseOnOrderUpdateEvent>
  <UseOnPositionUpdateEvent>false</UseOnPositionUpdateEvent>
  <UseOnRenderEvent>true</UseOnRenderEvent>
  <UseOnRestoreValuesEvent>false</UseOnRestoreValuesEvent>
  <UseOnShareEvent>true</UseOnShareEvent>
  <UseOnWindowCreatedEvent>false</UseOnWindowCreatedEvent>
  <UseOnWindowDestroyedEvent>false</UseOnWindowDestroyedEvent>
  <Variables>
    <InputParameter>
      <Default>false</Default>
      <Name>CanTrade</Name>
      <Type>bool</Type>
    </InputParameter>
    <InputParameter>
      <Default>0</Default>
      <Name>GapSize</Name>
      <Type>int</Type>
    </InputParameter>
  </Variables>
  <Name>FVW</Name>
</ScriptProperties>
@*/
#endregion
