Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$sourceUrl = 'https://strategyquant.com/wp-content/uploads/2026/06/ORB_Benchmark.sqx'
$workspaceTarget = Join-Path $PSScriptRoot 'ORB Optimizable Horarios EST.sqx'
$installedTarget = 'C:\StrategyQuantX144\user\settings\StrategyTemplates\ORB Optimizable Horarios EST.sqx'

function New-ExternalVariable {
    param(
        [xml]$Document,
        [string]$Id,
        [string]$Name,
        [string]$Value
    )

    $variable = $Document.CreateElement('variable')
    $variable.SetAttribute('makeExternal', 'true')
    foreach ($item in @(
        @{ Tag = 'id'; Text = $Id },
        @{ Tag = 'name'; Text = $Name },
        @{ Tag = 'type'; Text = 'int' },
        @{ Tag = 'value'; Text = $Value },
        @{ Tag = 'paramType'; Text = '' },
        @{ Tag = 'makeExternal'; Text = 'true' }
    )) {
        $node = $Document.CreateElement($item.Tag)
        if ($item.Tag -eq 'id') { $node.SetAttribute('variable', 'true') }
        $node.InnerText = $item.Text
        [void]$variable.AppendChild($node)
    }
    return $variable
}

function New-IntVariableItem {
    param([xml]$Document, [string]$VariableId)
    $fragment = $Document.CreateDocumentFragment()
    $fragment.InnerXml = @"
<Item customSnippet="false" key="IntVariable" name="(IVAR) Int Variable" display="#Variable#" returnType="number" ignoreInBuilder="true" mI="Other" categoryType="other" notFirstValue="true"><Param key="#Variable#" name="Variable" type="int" defaultValue="10" controlType="comboVar" builderStep="1" variable="true">$VariableId</Param></Item>
"@
    return $fragment.FirstChild
}

function Set-RightOperandToVariable {
    param([xml]$Document, $ComparisonNode, [string]$VariableId)
    $rightBlock = $ComparisonNode.SelectSingleNode("Block[@key='#Right#']")
    if ($null -eq $rightBlock) { throw "Right operand not found for $($ComparisonNode.key)" }
    $rightBlock.RemoveAll()
    $rightBlock.SetAttribute('key', '#Right#')
    $rightBlock.SetAttribute('name', 'Right')
    $rightBlock.SetAttribute('type', 'value')
    $rightBlock.SetAttribute('controlType', 'value')
    [void]$rightBlock.AppendChild((New-IntVariableItem -Document $Document -VariableId $VariableId))
}

function Set-ComparisonNumber {
    param($ComparisonNode, [string]$Value)
    $param = $ComparisonNode.SelectSingleNode("Block[@key='#Right#']/Item[@key='Number']/Param[@key='#Number#']")
    if ($null -ne $param) {
        $param.InnerText = $Value
        $param.SetAttribute('value', $Value)
    }
}

$client = [System.Net.WebClient]::new()
try { $sourceBytes = $client.DownloadData($sourceUrl) } finally { $client.Dispose() }

$sourceStream = [System.IO.MemoryStream]::new($sourceBytes)
$sourceArchive = [System.IO.Compression.ZipArchive]::new($sourceStream, [System.IO.Compression.ZipArchiveMode]::Read)
$outputStream = [System.IO.MemoryStream]::new()
$outputArchive = [System.IO.Compression.ZipArchive]::new($outputStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)

try {
    foreach ($entry in $sourceArchive.Entries) {
        # A Strategy Template only needs its editable strategy and defaults.
        # Omit the benchmark's historical results/orders so no stale performance
        # data is presented as if it belonged to this adapted template.
        if ($entry.FullName -notin @('strategy_Portfolio.xml', 'lastSettings.xml'))
            { continue }
        $input = $entry.Open()
        try {
            $isXml = $entry.FullName -in @('strategy_Portfolio.xml', 'lastSettings.xml')
            if ($isXml) {
                $reader = [System.IO.StreamReader]::new($input)
                $content = $reader.ReadToEnd()
                $reader.Dispose()
                [xml]$xml = $content

                if ($entry.FullName -eq 'strategy_Portfolio.xml') {
                    $xml.StrategyFile.options.StrategyName = 'ORB Optimizable Horarios EST'
                    $xml.StrategyFile.Strategy.Description = 'ORB intradia para SQX 144. Inicio y duracion del rango externos; ventana de trading optimizable desde Trading Options. Horas en zona de los datos (usar New York/EST).'

                    $variables = $xml.SelectSingleNode('//Strategy/Variables')
                    $rangeStartHourId = 'd74529c3-b87f-4c4b-9dd0-000000000930'
                    $rangeStartMinuteId = 'd74529c3-b87f-4c4b-9dd0-000000000030'
                    [void]$variables.AppendChild((New-ExternalVariable $xml $rangeStartHourId 'RangeStartHour_EST' '9'))
                    [void]$variables.AppendChild((New-ExternalVariable $xml $rangeStartMinuteId 'RangeStartMinute_EST' '30'))

                    $duration = $xml.SelectSingleNode("//Variables/variable[name='Openrangeminutes']/value")
                    if ($null -eq $duration) { throw 'Openrangeminutes variable not found.' }
                    $duration.InnerText = '15'

                    $initRule = $xml.SelectSingleNode("//Rule[@name='InitRange']")
                    $hourComparison = $initRule.SelectNodes('.//Item[@key="Equals"]') | Where-Object { $null -ne $_.SelectSingleNode('.//Item[@key="BarHour"]') } | Select-Object -First 1
                    $minuteComparison = $initRule.SelectNodes('.//Item[@key="IsGreaterOrEqual"]') | Where-Object { $null -ne $_.SelectSingleNode('.//Item[@key="BarMinute"]') } | Select-Object -First 1
                    Set-RightOperandToVariable $xml $hourComparison $rangeStartHourId
                    Set-RightOperandToVariable $xml $minuteComparison $rangeStartMinuteId

                    # Trading Options en lastSettings aplicará la ventana exacta.
                    # Neutralizamos el filtro horario redundante de las señales.
                    $signals = $xml.SelectSingleNode("//Rule[@name='Trading signals']")
                    foreach ($comparison in $signals.SelectNodes('.//Item[@key="IsGreaterOrEqual"]')) {
                        if ($null -ne $comparison.SelectSingleNode('.//Item[@key="BarHour"]') -or
                            $null -ne $comparison.SelectSingleNode('.//Item[@key="BarMinute"]')) {
                            Set-ComparisonNumber $comparison '0'
                        }
                    }
                    foreach ($comparison in $signals.SelectNodes('.//Item[@key="IsLower"]')) {
                        if ($null -ne $comparison.SelectSingleNode('.//Item[@key="BarHour"]')) {
                            Set-ComparisonNumber $comparison '24'
                        }
                    }
                }
                else {
                    $params = $xml.SelectNodes('//BuildTradingOptions/Params/Param')
                    foreach ($param in $params) {
                        switch ($param.key) {
                            'LimitTimeRange' { $param.InnerText = 'true'; $param.SetAttribute('value', 'true') }
                            'SignalTimeRangeFrom' { $param.InnerText = '35100'; $param.SetAttribute('value', '35100') } # 09:45
                            'SignalTimeRangeTo' { $param.InnerText = '39600'; $param.SetAttribute('value', '39600') }   # 11:00
                            'ExitAtEndOfRange' { $param.InnerText = 'false'; $param.SetAttribute('value', 'false') }
                            'MaxTradesPerDay' { $param.InnerText = '0'; $param.SetAttribute('value', '0') }
                        }
                    }
                }

                $newEntry = $outputArchive.CreateEntry($entry.FullName, [System.IO.Compression.CompressionLevel]::Optimal)
                $writer = [System.IO.StreamWriter]::new($newEntry.Open(), [System.Text.UTF8Encoding]::new($false))
                try { $xml.Save($writer) } finally { $writer.Dispose() }
            }
            else {
                $newEntry = $outputArchive.CreateEntry($entry.FullName, [System.IO.Compression.CompressionLevel]::Optimal)
                $output = $newEntry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            }
        }
        finally { $input.Dispose() }
    }
}
finally {
    $outputArchive.Dispose()
    $sourceArchive.Dispose()
    $sourceStream.Dispose()
}

$resultBytes = $outputStream.ToArray()
$outputStream.Dispose()
[System.IO.File]::WriteAllBytes($workspaceTarget, $resultBytes)
[System.IO.File]::WriteAllBytes($installedTarget, $resultBytes)

Get-Item -LiteralPath $workspaceTarget, $installedTarget | Select-Object FullName, Length, LastWriteTime
