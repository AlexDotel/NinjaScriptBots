Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-ZipText {
    param([string]$ZipPath, [string]$EntryName)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $archive.GetEntry($EntryName)
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

function Write-SingleEntryZip {
    param([string]$ZipPath, [string]$EntryName, [string]$Content)
    if (Test-Path -LiteralPath $ZipPath) { throw "Destination already exists: $ZipPath" }
    $archive = [System.IO.Compression.ZipFile]::Open(
        $ZipPath,
        [System.IO.Compression.ZipArchiveMode]::Create
    )
    try {
        $entry = $archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $writer = [System.IO.StreamWriter]::new($entry.Open(), [System.Text.UTF8Encoding]::new($false))
        try { $writer.Write($Content) } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
}

$templateSource = 'C:\StrategyQuantX144\user\settings\StrategyTemplates\highest_breakout_template_daily_filter.sqx'
$templateTarget = 'C:\StrategyQuantX144\user\settings\StrategyTemplates\NAS100 H1 Breakout Fixed Risk.sqx'
$configSource = 'C:\StrategyQuantX144\user\settings\Configs\NQ CFD H1 D1 Multi Timeframe.cfx'
$configTarget = 'C:\StrategyQuantX144\user\settings\Configs\NAS100 H1 Breakout MT5 FivePercent.cfx'

Remove-Item -LiteralPath $templateTarget, $configTarget -Force -ErrorAction SilentlyContinue

$sourceArchive = [System.IO.Compression.ZipFile]::OpenRead($templateSource)
$targetArchive = [System.IO.Compression.ZipFile]::Open(
    $templateTarget,
    [System.IO.Compression.ZipArchiveMode]::Create
)
try {
    foreach ($sourceEntry in $sourceArchive.Entries) {
        $targetEntry = $targetArchive.CreateEntry(
            $sourceEntry.FullName,
            [System.IO.Compression.CompressionLevel]::Optimal
        )
        $inputStream = $sourceEntry.Open()
        $outputStream = $targetEntry.Open()
        try {
            if ($sourceEntry.FullName -eq 'strategy_Portfolio.xml') {
                $reader = [System.IO.StreamReader]::new($inputStream)
                $content = $reader.ReadToEnd()
                $reader.Dispose()
                $content = $content.Replace(
                    '<StrategyName>highest template - D1 filter timelimit5</StrategyName>',
                    '<StrategyName>NAS100 H1 Breakout Fixed Risk</StrategyName>'
                )
                $writer = [System.IO.StreamWriter]::new($outputStream, [System.Text.UTF8Encoding]::new($false))
                $writer.Write($content)
                $writer.Dispose()
            } else {
                $inputStream.CopyTo($outputStream)
            }
        } finally {
            $inputStream.Dispose()
            $outputStream.Dispose()
        }
    }
} finally {
    $sourceArchive.Dispose()
    $targetArchive.Dispose()
}

$configXml = Read-ZipText -ZipPath $configSource -EntryName 'config.xml'
$configXml = $configXml.Replace('templateFile="C:\_BETA\141RC1\user\settings\Configs\NQ H1 D1 MULTI-TIMEFRAME MT5.cfx"', 'templateFile="NAS100 H1 Breakout MT5 FivePercent.cfx"')
$configXml = $configXml.Replace(
    'templateFile="highest_breakout_template_daily_filter.sqx"',
    'templateFile="NAS100 H1 Breakout Fixed Risk.sqx"'
)
$configXml = $configXml.Replace(
    '<Chart symbol="NQ_M1_dukas" timeframe="H1" spread="1503"/><Chart symbol="NQ_M1_dukas" timeframe="D1" spread="1503"/>',
    '<Chart symbol="NAS100_DK_UTC2" timeframe="H1" spread="157"/><Chart symbol="NAS100_DK_UTC2" timeframe="D1" spread="157"/>'
)
$configXml = $configXml.Replace(
    'dateFrom="2017.01.01" dateTo="2022.06.30" testPrecision="2" session="No Session" slippage="1000" minDist="0" engine="MetaTrader5 (hedged)"',
    'dateFrom="2018.01.01" dateTo="2025.12.31" testPrecision="1" session="USATECH.IDX_dukascopy" slippage="100" minDist="0" engine="MetaTrader5 (hedged)"'
)
$configXml = $configXml.Replace('<Range dateFrom="2020.11.05" dateTo="2022.06.30"/>', '<Range dateFrom="2024.01.01" dateTo="2025.12.31"/>')
$configXml = $configXml.Replace('<MinSLATRMultiple>1.5</MinSLATRMultiple>', '<MinSLATRMultiple>1.2</MinSLATRMultiple>')
$configXml = $configXml.Replace('<MinPTATRMultiple>2</MinPTATRMultiple>', '<MinPTATRMultiple>1.5</MinPTATRMultiple>')

Write-SingleEntryZip -ZipPath $configTarget -EntryName 'config.xml' -Content $configXml

Get-Item -LiteralPath $templateTarget, $configTarget |
    Select-Object FullName, Length, LastWriteTime
