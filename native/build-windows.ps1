# Run on Windows x64 with a GCC/MinGW-w64 compiler on PATH.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force -Path "win-x64" | Out-Null
gcc -std=c11 -O2 -Wall -Wextra -shared bonus.c -o win-x64/matchbonus.dll
if ($LASTEXITCODE -ne 0) { throw "Native build failed" }
Write-Host "Built native/win-x64/matchbonus.dll"
