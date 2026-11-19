[CmdletBinding()]
param(
    [ValidateSet('Phase2M4', 'Phase2M3')]
    [string]$RunId = 'Phase2M4',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$resultsRoot = Join-Path $projectRoot 'results'
$runPath = [IO.Path]::GetFullPath((Join-Path $resultsRoot $RunId))
$archiveRoot = [IO.Path]::GetFullPath((Join-Path $resultsRoot 'restart_backups'))
$backupPath = [IO.Path]::GetFullPath((Join-Path $archiveRoot ($RunId + '_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))))
$sourcePath = Join-Path $resultsRoot 'Phase2_initialization/Phase1M-1000010.pt'
$configPath = 'Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch.yaml'
$trainerPath = 'Assets/Scripts/AI/Training/train_phase2.py'

if (-not $runPath.StartsWith($resultsRoot + '\') -or
    -not $backupPath.StartsWith($archiveRoot + '\') -or
    $runPath -ne (Join-Path $resultsRoot $RunId)) {
    throw 'Resolved restart target is outside the expected results directory.'
}
if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Missing Phase1M initialization: $sourcePath" }
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $configPath))) { throw 'Training configuration is missing.' }
Write-Output "Restart target: $runPath"
Write-Output 'Initialization: Phase1M step 1000010; Phase 2 step counter and curriculum restart at zero.'
if (Test-Path -LiteralPath $runPath) { Write-Output "Recoverable backup: $backupPath" }
else { Write-Output 'No previous results exist for this run ID.' }
Write-Output "Command: uv run python $trainerPath $configPath --run-id=$RunId --force"
if ($DryRun) { return }

$active = @()
try {
    $active = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.Name -match '^(python|uv|mlagents-learn)(\.exe)?$' -and
        $_.CommandLine -match 'mlagents-learn|train_phase2.py' -and
        $_.CommandLine -match ('--run-id(?:=|\s+)' + [regex]::Escape($RunId) + '(?:\s|"|$)')
    })
}
catch { Write-Warning 'Process inventory unavailable; the trainer port check below is still required.' }
if ($active.Count -gt 0) { throw "Trainer $RunId is still running. Stop it with Ctrl+C and stop Unity Play Mode before restarting." }
$portProbe = [System.Net.Sockets.Socket]::new([System.Net.Sockets.AddressFamily]::InterNetwork,
    [System.Net.Sockets.SocketType]::Stream, [System.Net.Sockets.ProtocolType]::Tcp)
try {
    $portProbe.ExclusiveAddressUse = $true
    $portProbe.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 5004))
}
finally { $portProbe.Dispose() }
if (Test-Path -LiteralPath $runPath) {
    if (Test-Path -LiteralPath $backupPath) { throw 'Backup target already exists.' }
    New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
    Move-Item -LiteralPath $runPath -Destination $backupPath
    Write-Output "Previous run archived; recoverable at: $backupPath"
}
Push-Location -LiteralPath $projectRoot
try {
    & uv run python $trainerPath $configPath "--run-id=$RunId" --force
    if ($LASTEXITCODE -ne 0) { throw "Trainer exited with code $LASTEXITCODE; any backup is preserved." }
}
finally { Pop-Location }
