param(
    [string]$YoloProject,
    [string]$Python,
    [string]$VCRedistDir
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (-not $YoloProject) {
    $YoloProject = Join-Path (Split-Path -Parent $repository) 'YOLO/ultralytics-mokuiyolo11/ultralytics-mokuiyolo11/ultralytics-yolo11-main'
}
if (-not $Python) {
    $Python = Join-Path $env:LOCALAPPDATA 'AMLabSlicer/Monitor/venv/Scripts/python.exe'
}

$source = Join-Path $YoloProject 'ultralytics'
$run = 'xin -yolo11-1p2' + [char]0xff0b + 'emscp-lscd-wEIoUv3-3002'
$weights = Join-Path $YoloProject "runs/train/$run/weights/best.pt"
foreach ($path in @((Join-Path $source 'cfg/default.yaml'), $weights, $Python)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required engine input not found: $path" }
}
$site = & $Python -c "import sys,sysconfig,torch,grpc_tools; assert sys.version_info[:2] == (3,11) and sys.maxsize > 2**32 and torch.version.cuda is None; print(sysconfig.get_path('purelib'))"
if ($LASTEXITCODE -ne 0) { throw 'A working x64 Python 3.11 CPU environment is required to prepare the engine.' }

if (-not $VCRedistDir) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($visualStudio) {
            $redist = Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC/Redist/MSVC') -Directory |
                Where-Object Name -Match '^\d+\.' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
            if ($redist) { $VCRedistDir = Join-Path $redist.FullName 'x64' }
        }
    }
}
if (-not $VCRedistDir -or !(Test-Path -LiteralPath (Join-Path $VCRedistDir 'Microsoft.VC143.CRT/msvcp140.dll'))) {
    throw 'Pass -VCRedistDir with the x64 Visual C++ redistributable directory.'
}

$runtime = Join-Path $PSScriptRoot 'runtime'
$stage = Join-Path $PSScriptRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
$pythonDir = Join-Path $stage 'python'
New-Item -ItemType Directory -Path $pythonDir,(Join-Path $stage 'models') -Force | Out-Null

function Copy-Tree($from, $to, $excludedDirectories) {
    & robocopy $from $to /E /XD @excludedDirectories /XF '*.pyc' '*.pdb' '*.lib' /NFL /NDL /NJH /NJS /NP /R:1 /W:1 /MT:16 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed to copy $from" }
}

try {
    $zip = Join-Path $stage 'python-embed.zip'
    Invoke-WebRequest -UseBasicParsing 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip' -OutFile $zip
    if ((Get-FileHash -LiteralPath $zip -Algorithm MD5).Hash -ne '6D9AA08531D48FCC261BA667E2DF17C4') {
        throw 'The CPython archive does not match the checksum published by python.org.'
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $pythonDir
    Remove-Item -LiteralPath $zip
    Get-ChildItem -LiteralPath (Join-Path $VCRedistDir 'Microsoft.VC143.CRT') -Filter '*.dll' -File |
        Copy-Item -Destination $pythonDir
    Set-Content -LiteralPath (Join-Path $pythonDir 'python311._pth') -Encoding ASCII -Value @(
        'python311.zip', '.', 'Lib/site-packages'
    )

    Copy-Tree $site (Join-Path $pythonDir 'Lib/site-packages') @('__pycache__', (Join-Path $site 'torch/include'), (Join-Path $site 'pip'))
    Copy-Tree $source (Join-Path $stage 'ultralytics') @('__pycache__')
    $ultralyticsSettings = Join-Path $stage 'ultralytics/utils/__init__.py'
    $settings = [IO.File]::ReadAllText($ultralyticsSettings)
    [IO.File]::WriteAllText($ultralyticsSettings, $settings.Replace('ONLINE = is_online()', 'ONLINE = False  # Bundled inference uses local assets.'))
    Copy-Item -LiteralPath $weights -Destination (Join-Path $stage 'models/best.pt')
    Copy-Item -LiteralPath (Join-Path $YoloProject 'LICENSE') -Destination (Join-Path $stage 'LICENSE-Ultralytics.txt')

    & (Join-Path $pythonDir 'python.exe') -B -c "import torch,torchvision,grpc_tools,cv2; print(torch.__version__)"
    if ($LASTEXITCODE -ne 0) { throw 'The bundled Python runtime failed its dependency check.' }
    if (Test-Path -LiteralPath $runtime) {
        $previous = Join-Path $PSScriptRoot ('stage-previous-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $runtime -Destination $previous
        try { Move-Item -LiteralPath $stage -Destination $runtime }
        catch { Move-Item -LiteralPath $previous -Destination $runtime; throw }
        if ((Resolve-Path -LiteralPath $previous).Path -eq $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    } else {
        Move-Item -LiteralPath $stage -Destination $runtime
    }
    Write-Output "YOLO11 runtime prepared at $runtime"
}
finally {
    if ((Test-Path -LiteralPath $stage) -and (Resolve-Path -LiteralPath $stage).Path -eq $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
exit 0
