$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$install = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$install) { throw 'MSVC x64 C++ build tools are required for AtomSlicer.' }
$vcvars = Join-Path $install 'VC/Auxiliary/Build/vcvars64.bat'
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source = Join-Path $PSScriptRoot 'upstream/src'
$command = @"
@echo off
chcp 65001 > nul
call "$vcvars"
if errorlevel 1 exit /b 1
cd /d "$output"
cl /nologo /std:c++17 /EHsc /O2 /MD /utf-8 /Fe:atom_slicer.exe "$source\*.cpp" > build.log 2>&1
exit /b %errorlevel%
"@
$commandPath = Join-Path $output 'build.cmd'
[IO.File]::WriteAllText($commandPath, ($command -replace "`r?`n", "`r`n"), [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $commandPath
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $output 'build.log') -Tail 40; throw 'AtomSlicer native build failed.' }
