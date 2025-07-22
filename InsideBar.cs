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
	public class InsideBar : Strategy
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "InsideBar";
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
			}
			else if (State == State.Configure)
			{
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			if (CurrentBars[0] < 2)
				return;

			 // Set 1
			if (
				 // Vela Anterior Alcista
				(Close[2] > Open[2])
				 // Vela Bajista
				 && (Close[1] < Open[1])
				 // Inside Bajista
				 && ((Open[1] <= Close[2])
				 && (Close[1] > Open[2])))
			{
				Draw.ArrowDown(this, @"InsideBar Arrow down_1 " + Convert.ToString(CurrentBars[0]), false, 1, (High[1] + 5) , Brushes.Red);
			}
			
			 // Set 2
			if (
				 // Vela Anterior Bajista
				(Close[2] < Open[2])
				 // Vela Alcista
				 && (Close[1] > Open[1])
				 // Inside Alcista
				 && ((Open[1] >= Close[2])
				 && (Close[1] < Open[2])))
			{
				Draw.ArrowUp(this, @"InsideBar Arrow up_1 " + Convert.ToString(CurrentBars[0]), false, 1, (Low[1] - 5) , Brushes.Lime);
			}
			
		}
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
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Arrow down</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <BarsAgo>1</BarsAgo>
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
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
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
                <Date>2025-07-08T15:24:49.3944504</Date>
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
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>InsideBar</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Index>1</Index>
                  <StringValue>Arrow down_1</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Action>
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
                    <Date>2025-07-08T15:25:14.3721892</Date>
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
            <VariableDateTime>2025-07-08T15:24:49.3944504</VariableDateTime>
            <VariableBool>false</VariableBool>
            <Y>
              <LiveValue xsi:type="xsd:string">(High[1] + 5) </LiveValue>
              <BindingValue xsi:type="xsd:string">(High[1] + 5) </BindingValue>
              <DefaultValue>0</DefaultValue>
              <IsInt>false</IsInt>
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
                <Date>2025-07-08T15:25:30.9814432</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <OffsetOperator>Add</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                  </ConditionOffset>
                  <Offset>
                    <LiveValue xsi:type="xsd:string">5</LiveValue>
                    <BindingValue xsi:type="xsd:string">5</BindingValue>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <IsLiteral>true</IsLiteral>
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
      </Actions>
      <ActiveAction>
        <IsExpanded>false</IsExpanded>
        <IsSelected>true</IsSelected>
        <Name>Arrow down</Name>
        <OffsetType>Arithmetic</OffsetType>
        <ActionProperties>
          <BarsAgo>1</BarsAgo>
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
            <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
            <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
            <DefaultValue>0</DefaultValue>
            <IsInt>true</IsInt>
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
              <Date>2025-07-08T15:24:49.3944504</Date>
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
          </Quantity>
          <ServiceName />
          <ScreenshotPath />
          <SoundLocation />
          <Tag>
            <SeparatorCharacter> </SeparatorCharacter>
            <Strings>
              <NinjaScriptString>
                <Index>0</Index>
                <StringValue>InsideBar</StringValue>
              </NinjaScriptString>
              <NinjaScriptString>
                <Index>1</Index>
                <StringValue>Arrow down_1</StringValue>
              </NinjaScriptString>
              <NinjaScriptString>
                <Action>
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
                  <Date>2025-07-08T15:25:14.3721892</Date>
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
          <VariableDateTime>2025-07-08T15:24:49.3944504</VariableDateTime>
          <VariableBool>false</VariableBool>
          <Y>
            <LiveValue xsi:type="xsd:string">(High[1] + 5) </LiveValue>
            <BindingValue xsi:type="xsd:string">(High[1] + 5) </BindingValue>
            <DefaultValue>0</DefaultValue>
            <IsInt>false</IsInt>
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
              <Date>2025-07-08T15:25:30.9814432</Date>
              <DayOfWeek>Sunday</DayOfWeek>
              <EndBar>0</EndBar>
              <ForceSeriesIndex>false</ForceSeriesIndex>
              <LookBackPeriod>0</LookBackPeriod>
              <MarketPosition>Long</MarketPosition>
              <OffsetBuilder>
                <ConditionOffset>
                  <OffsetOperator>Add</OffsetOperator>
                  <OffsetType>Arithmetic</OffsetType>
                  <IsSetEnabled>false</IsSetEnabled>
                  <OffsetValue>0</OffsetValue>
                </ConditionOffset>
                <Offset>
                  <LiveValue xsi:type="xsd:string">5</LiveValue>
                  <BindingValue xsi:type="xsd:string">5</BindingValue>
                  <DefaultValue>0</DefaultValue>
                  <IsInt>false</IsInt>
                  <IsLiteral>true</IsLiteral>
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
      </ActiveAction>
      <AnyOrAll>All</AnyOrAll>
      <Conditions>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Close</Name>
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
                <Date>2025-07-08T15:15:10.510514</Date>
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
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Greater</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:15:10.5292254</Date>
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
                  <PriceType>Open</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Vela Anterior Alcista</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Close</Name>
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
                <Date>2025-07-08T15:15:10.510514</Date>
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
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Less</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:15:10.5292254</Date>
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
                  <PriceType>Open</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Vela Bajista</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:22:40.2086611</Date>
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
                  <PriceType>Open</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>LessEqual</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Close</Name>
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
                <Date>2025-07-08T15:22:40.2258782</Date>
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
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Close</Name>
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
                <Date>2025-07-08T15:23:15.4128778</Date>
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
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Greater</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:23:15.4298898</Date>
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
                  <PriceType>Open</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Inside Bajista</DisplayName>
        </WizardConditionGroup>
      </Conditions>
      <SetName>Set 1</SetName>
      <SetNumber>1</SetNumber>
    </ConditionalAction>
    <ConditionalAction>
      <Actions>
        <WizardAction>
          <IsExpanded>false</IsExpanded>
          <IsSelected>true</IsSelected>
          <Name>Arrow up</Name>
          <OffsetType>Arithmetic</OffsetType>
          <ActionProperties>
            <BarsAgo>1</BarsAgo>
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
              <LiveValue xsi:type="xsd:string">DefaultQuantity</LiveValue>
              <BindingValue xsi:type="xsd:string">DefaultQuantity</BindingValue>
              <DefaultValue>0</DefaultValue>
              <IsInt>true</IsInt>
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
                <Date>2025-07-08T15:25:50.6272388</Date>
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
            </Quantity>
            <ServiceName />
            <ScreenshotPath />
            <SoundLocation />
            <Tag>
              <SeparatorCharacter> </SeparatorCharacter>
              <Strings>
                <NinjaScriptString>
                  <Index>0</Index>
                  <StringValue>InsideBar</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Index>1</Index>
                  <StringValue>Arrow up_1</StringValue>
                </NinjaScriptString>
                <NinjaScriptString>
                  <Action>
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
                    <Date>2025-07-08T15:26:05.504963</Date>
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
            <VariableDateTime>2025-07-08T15:25:50.6272388</VariableDateTime>
            <VariableBool>false</VariableBool>
            <Y>
              <LiveValue xsi:type="xsd:string">(Low[1] - 5) </LiveValue>
              <BindingValue xsi:type="xsd:string">(Low[1] - 5) </BindingValue>
              <DefaultValue>0</DefaultValue>
              <IsInt>false</IsInt>
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
                <BarsAgo>1</BarsAgo>
                <CurrencyType>Currency</CurrencyType>
                <Date>2025-07-08T15:26:18.1570253</Date>
                <DayOfWeek>Sunday</DayOfWeek>
                <EndBar>0</EndBar>
                <ForceSeriesIndex>false</ForceSeriesIndex>
                <LookBackPeriod>0</LookBackPeriod>
                <MarketPosition>Long</MarketPosition>
                <OffsetBuilder>
                  <ConditionOffset>
                    <OffsetOperator>Subtract</OffsetOperator>
                    <OffsetType>Arithmetic</OffsetType>
                    <IsSetEnabled>false</IsSetEnabled>
                    <OffsetValue>0</OffsetValue>
                  </ConditionOffset>
                  <Offset>
                    <LiveValue xsi:type="xsd:string">5</LiveValue>
                    <BindingValue xsi:type="xsd:string">5</BindingValue>
                    <DefaultValue>0</DefaultValue>
                    <IsInt>false</IsInt>
                    <IsLiteral>true</IsLiteral>
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
      </Actions>
      <AnyOrAll>All</AnyOrAll>
      <Conditions>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Close</Name>
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
                <Date>2025-07-08T15:15:10.510514</Date>
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
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Less</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:15:10.5292254</Date>
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
                  <PriceType>Open</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Vela Anterior Bajista</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Close</Name>
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
                <Date>2025-07-08T15:15:10.510514</Date>
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
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Greater</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:15:10.5292254</Date>
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
                  <PriceType>Open</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Vela Alcista</DisplayName>
        </WizardConditionGroup>
        <WizardConditionGroup>
          <AnyOrAll>All</AnyOrAll>
          <Conditions>
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:22:40.2086611</Date>
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
                  <PriceType>Open</PriceType>
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
                <Name>Close</Name>
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
                <Date>2025-07-08T15:22:40.2258782</Date>
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
            <WizardCondition>
              <LeftItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Close</Name>
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
                <Date>2025-07-08T15:23:15.4128778</Date>
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
              </LeftItem>
              <Lookback>1</Lookback>
              <Operator>Less</Operator>
              <RightItem xsi:type="WizardConditionItem">
                <Children />
                <IsExpanded>false</IsExpanded>
                <IsSelected>true</IsSelected>
                <Name>Open</Name>
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
                <Date>2025-07-08T15:23:15.4298898</Date>
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
                  <PriceType>Open</PriceType>
                  <SeriesType>DefaultSeries</SeriesType>
                </Series1>
                <StartBar>0</StartBar>
                <State>Undefined</State>
                <Time>0001-01-01T00:00:00</Time>
              </RightItem>
            </WizardCondition>
          </Conditions>
          <IsGroup>true</IsGroup>
          <DisplayName>Inside Alcista</DisplayName>
        </WizardConditionGroup>
      </Conditions>
      <SetName>Set 2</SetName>
      <SetNumber>2</SetNumber>
    </ConditionalAction>
  </ConditionalActions>
  <CustomSeries />
  <DataSeries />
  <Description>Enter the description for your new custom Strategy here.</Description>
  <DisplayInDataBox>true</DisplayInDataBox>
  <DrawHorizontalGridLines>true</DrawHorizontalGridLines>
  <DrawOnPricePanel>true</DrawOnPricePanel>
  <DrawVerticalGridLines>true</DrawVerticalGridLines>
  <EntriesPerDirection>1</EntriesPerDirection>
  <EntryHandling>AllEntries</EntryHandling>
  <ExitOnSessionClose>true</ExitOnSessionClose>
  <ExitOnSessionCloseSeconds>30</ExitOnSessionCloseSeconds>
  <FillLimitOrdersOnTouch>false</FillLimitOrdersOnTouch>
  <InputParameters />
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
  <StopsAndTargets />
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
  <Variables />
  <Name>InsideBar</Name>
</ScriptProperties>
@*/
#endregion
