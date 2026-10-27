param(
    [string]$GameData = 'E:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data',
    [string]$Mo2Profile = 'D:\ModOrganizer2-STR-Test\profiles\STR Trade Test',
    [string]$PreviousCatalog = '',
    [string]$GameCosave = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$candidate = Join-Path $repo 'build\catalog-expanded'
$pluginFolder = Join-Path $candidate 'Data\SKSE\Plugins'
$nativeDll = Join-Path $repo 'src\SkyrimTreader.NativeEconomy\bin\Release\net8.0\win-x64\publish\SkyrimTreader.NativeEconomy.dll'
$skseDll = Join-Path $repo 'build\skse-trade-events\Release\SimulatedTradeRoutes.dll'
$bridgeExe = Join-Path $repo 'build\native-bridge-checks\Release\STRNativeBridgeChecks.exe'
$serialExe = Join-Path $repo 'build\skse-serialization-checks\Release\STRSKSESerializationChecks.exe'
if (-not $PreviousCatalog) {
    $PreviousCatalog = Join-Path $repo 'build\live-price-package\Data\SKSE\Plugins\STRCatalog.json'
}
if (-not (Test-Path -LiteralPath $GameData -PathType Container)) { throw "Game Data folder not found: $GameData" }
if (-not (Test-Path -LiteralPath $Mo2Profile -PathType Container)) { throw "MO2 profile not found: $Mo2Profile" }
if (-not (Test-Path -LiteralPath $PreviousCatalog -PathType Leaf)) { throw "Previous catalog not found: $PreviousCatalog" }
if (-not $candidate.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate folder is outside the repository.' }

function Invoke-Checked([string]$program, [string[]]$arguments) {
    & $program @arguments
    if ($LASTEXITCODE -ne 0) { throw "$program exited with code $LASTEXITCODE" }
}

New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
Push-Location $repo
try {
    Invoke-Checked 'dotnet' @('run', '--project', '.\src\SkyrimTreader.Catalog', '--no-restore', '--',
        '--mo2-profile', $GameData, $Mo2Profile, '.\data\item-rules.json', (Join-Path $candidate 'STRCatalog.json'))
    Invoke-Checked 'dotnet' @('restore', '.\src\SkyrimTreader.NativeEconomy', '-r', 'win-x64',
        '--configfile', '.\build\native-aot-nuget.config')
    Invoke-Checked 'dotnet' @('publish', '.\src\SkyrimTreader.NativeEconomy', '-c', 'Release', '-r', 'win-x64', '--no-restore')
    Invoke-Checked 'dotnet' @('run', '--project', '.\tests\SkyrimTreader.Simulation.Checks', '--no-restore')
    Invoke-Checked 'dotnet' @('run', '--project', '.\tests\SkyrimTreader.NativeEconomy.Checks', '--no-restore', '--', $nativeDll)
    $sdk = Join-Path $repo 'build\skse-sdk\skse64-2.3.1'
    Invoke-Checked 'cmake' @('-S', '.\src\SkyrimTreader.Runtime', '-B', '.\build\skse-trade-events',
        '-G', 'Visual Studio 17 2022', '-A', 'x64', "-DSKSE_SDK_ROOT=$sdk",
        '-DSTR_NATIVE_ECONOMY=ON', '-DSTR_LIVE_PRICES=ON', '-DSTR_TRADE_EVENTS=ON')
    Invoke-Checked 'cmake' @('--build', '.\build\skse-trade-events', '--config', 'Release')
    Invoke-Checked 'cmake' @('-S', '.\tests\SkyrimTreader.NativeEconomy.BridgeChecks',
        '-B', '.\build\native-bridge-checks', '-G', 'Visual Studio 17 2022', '-A', 'x64')
    Invoke-Checked 'cmake' @('--build', '.\build\native-bridge-checks', '--config', 'Release')
    Invoke-Checked 'cmake' @('-S', '.\tests\SkyrimTreader.TradeCapture.Checks',
        '-B', '.\build\trade-capture-checks', '-G', 'Visual Studio 17 2022', '-A', 'x64')
    Invoke-Checked 'cmake' @('--build', '.\build\trade-capture-checks', '--config', 'Release')
    Invoke-Checked (Join-Path $repo 'build\trade-capture-checks\Release\STRTradeCaptureChecks.exe') @()
    Copy-Item -LiteralPath $skseDll -Destination (Join-Path $pluginFolder 'SimulatedTradeRoutes.dll') -Force
    Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $pluginFolder 'SkyrimTreader.NativeEconomy.dll') -Force
    Copy-Item -LiteralPath (Join-Path $candidate 'STRCatalog.json') -Destination (Join-Path $pluginFolder 'STRCatalog.json') -Force
    Copy-Item -LiteralPath '.\data\mvp-economy.json' -Destination (Join-Path $pluginFolder 'STRConfig.json') -Force
    Copy-Item -LiteralPath '.\data\mvp-runtime-settings.json' -Destination (Join-Path $pluginFolder 'STRSettings.json') -Force
    Copy-Item -LiteralPath '.\data\visible-caravan-routes.json' -Destination (Join-Path $pluginFolder 'STRVisibleRoutes.json') -Force
    Copy-Item -LiteralPath $PreviousCatalog -Destination (Join-Path $pluginFolder 'STRPreviousCatalog.json') -Force

    Invoke-Checked $bridgeExe @($pluginFolder)
    $snapshot = Join-Path $candidate 'prices-day5.json'
    Invoke-Checked 'dotnet' @('run', '--project', '.\src\SkyrimTreader.Cli', '--no-restore', '--',
        'export-prices', '.\data\mvp-economy.json', (Join-Path $candidate 'STRCatalog.json'), '5', $snapshot)
    Invoke-Checked 'dotnet' @('run', '--project', '.\src\SkyrimTreader.Cli', '--no-restore', '--',
        'verify-prices', '.\data\mvp-economy.json', (Join-Path $candidate 'STRCatalog.json'), $snapshot)
    $simulationState = Join-Path $candidate 'simulation-state-day5.json'
    if (-not $simulationState.StartsWith($candidate, [StringComparison]::OrdinalIgnoreCase)) { throw 'Simulation state is outside candidate folder.' }
    if (Test-Path -LiteralPath $simulationState) { Remove-Item -LiteralPath $simulationState -Force }
    Invoke-Checked 'dotnet' @('run', '--project', '.\src\SkyrimTreader.Cli', '--no-restore', '--',
        '.\data\mvp-economy.json', '5', $simulationState)
    Invoke-Checked 'dotnet' @('run', '--project', '.\src\SkyrimTreader.Cli', '--no-restore', '--',
        'export-status', '.\data\mvp-economy.json', $simulationState, (Join-Path $candidate 'status-day5.json'))
    if ($GameCosave) {
        Invoke-Checked 'cmake' @('-S', '.\src\SkyrimTreader.Runtime', '-B', '.\build\skse-native-economy',
            '-G', 'Visual Studio 17 2022', '-A', 'x64', "-DSKSE_SDK_ROOT=$sdk", '-DSTR_NATIVE_ECONOMY=ON')
        Invoke-Checked 'cmake' @('--build', '.\build\skse-native-economy', '--config', 'Release')
        Invoke-Checked 'cmake' @('-S', '.\tests\SkyrimTreader.SKSESerialization.Checks',
            '-B', '.\build\skse-serialization-checks', '-G', 'Visual Studio 17 2022', '-A', 'x64', "-DSKSE_SDK_ROOT=$sdk")
        Invoke-Checked 'cmake' @('--build', '.\build\skse-serialization-checks', '--config', 'Release')
        $harness = Join-Path $candidate 'harness'
        New-Item -ItemType Directory -Path $harness -Force | Out-Null
        Copy-Item -LiteralPath '.\build\skse-native-economy\Release\SimulatedTradeRoutes.dll' -Destination (Join-Path $harness 'SimulatedTradeRoutes.dll') -Force
        foreach ($name in @('SkyrimTreader.NativeEconomy.dll', 'STRCatalog.json', 'STRConfig.json', 'STRSettings.json', 'STRVisibleRoutes.json', 'STRPreviousCatalog.json')) {
            Copy-Item -LiteralPath (Join-Path $pluginFolder $name) -Destination (Join-Path $harness $name) -Force
        }
        Invoke-Checked $serialExe @($harness, $GameCosave)
    }
    Write-Output "Candidate ready: $pluginFolder"
}
finally { Pop-Location }
