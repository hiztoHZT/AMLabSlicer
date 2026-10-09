param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$VcpkgRoot = $env:VCPKG_ROOT,
    [switch]$Fresh
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$VcpkgRoot) { $VcpkgRoot = [Environment]::GetEnvironmentVariable('VCPKG_ROOT', 'User') }
if (!(Test-Path "$VcpkgRoot/scripts/buildsystems/vcpkg.cmake")) { throw 'Set VCPKG_ROOT to the installed vcpkg directory.' }
$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$instances = & $vswhere -all -products '*' -format json | ConvertFrom-Json
$vs = $instances | Where-Object {
    (Test-Path "$($_.installationPath)/MSBuild/Current/Bin/SdkResolvers/Microsoft.DotNet.MSBuildSdkResolver") -and
    (Test-Path "$($_.installationPath)/VC/Tools/MSVC")
} | Select-Object -First 1
if (!$vs) { throw 'Visual Studio with both .NET SDK resolver and C++ tools is required.' }
$msbuild = "$($vs.installationPath)/MSBuild/Current/Bin/amd64/MSBuild.exe"
$cmake = "$($vs.installationPath)/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"
if (!(Test-Path $cmake)) { $cmake = (Get-Command cmake -ErrorAction Stop).Source }
Push-Location $repo
try {
    & $msbuild AMLabSlicer.sln /restore "/p:Configuration=$Configuration" /p:Platform=x64 "/p:VcpkgRoot=$VcpkgRoot\" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    $cmakeArgs = @('-S', 'AMLabSlicer.Engine.FDM', '-B', 'AMLabSlicer.Engine.FDM/build', '-G', 'Visual Studio 17 2022', '-A', 'x64', "-DCMAKE_TOOLCHAIN_FILE=$VcpkgRoot/scripts/buildsystems/vcpkg.cmake")
    if ($Fresh) { $cmakeArgs += '--fresh' }
    & $cmake @cmakeArgs
    if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed; after moving the checkout, rerun with -Fresh.' }
    # EngineHost currently searches build/Release regardless of the UI configuration.
    & $cmake --build AMLabSlicer.Engine.FDM/build --config Release --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'FDM build failed.' }
} finally { Pop-Location }
