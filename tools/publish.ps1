# Publishes the app as a Native AOT, self-contained folder. Run from the repo root:
#   powershell -File tools/publish.ps1 [-Runtime win-x64|win-arm64] [-Out <folder>]
# The result is a folder (not a single file — WinUI needs its native DLLs and .pri files next to the exe).
param(
    [string]$Runtime = "win-x64",
    [string]$Out = "src/WirelessStatus.App/bin/publish"
)
$ErrorActionPreference = "Stop"

# The AOT linker locates the MSVC toolchain through vswhere.exe, which the VS installer doesn't put on PATH.
$installer = Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Microsoft Visual Studio\Installer"
if (Test-Path (Join-Path $installer "vswhere.exe")) { $env:PATH = "$installer;$env:PATH" }

$platform = if ($Runtime -eq "win-arm64") { "ARM64" } else { "x64" }
dotnet publish src/WirelessStatus.App -c Release -r $Runtime -p:Platform=$platform -o $Out -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$size = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Published to $Out ({0:N0} MB)" -f $size
