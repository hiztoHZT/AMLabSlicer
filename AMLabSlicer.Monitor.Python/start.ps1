param(
    [switch]$Setup,
    [string]$Python,
    [string]$Device = 'cpu'
)

$ErrorActionPreference = 'Stop'
$venv = Join-Path $env:LOCALAPPDATA 'AMLabSlicer\Monitor\venv'
if ($Setup -and $Python) {
    throw 'Use -Setup for the dedicated environment or -Python for an existing environment.'
}
if (-not $Python) {
    $Python = Join-Path $venv 'Scripts\python.exe'
}

if ($Setup) {
    if (-not (Test-Path -LiteralPath $Python)) {
        py -3.11 -m venv $venv
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    & $Python -m pip install --index-url https://download.pytorch.org/whl/cpu 'torch==2.8.0' 'torchvision==0.23.0'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $Python -m pip install -r (Join-Path $PSScriptRoot 'requirements.txt')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Get-Command $Python -ErrorAction SilentlyContinue)) {
    throw 'Run start.ps1 -Setup once, or pass -Python with the path to the YOLO environment.'
}
& $Python (Join-Path $PSScriptRoot 'server.py') --device $Device
exit $LASTEXITCODE
