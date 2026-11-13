# Run from PowerShell on Windows x64. Requires x64 GCC (MinGW-w64) OR MSVC cl.exe.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force -Path "win-x64" | Out-Null
if (Get-Command gcc -ErrorAction SilentlyContinue) {
    & gcc -std=c11 -O2 -Wall -Wextra -shared bonus.c -o win-x64/matchbonus.dll
    if ($LASTEXITCODE -ne 0) { throw "gcc failed" }
} elseif (Get-Command cl -ErrorAction SilentlyContinue) {
    & cl /nologo /O2 /LD /Fe:win-x64/matchbonus.dll bonus.c
    if ($LASTEXITCODE -ne 0) { throw "cl.exe failed" }
} else {
    throw "No C compiler found. Install x64 MinGW-w64 GCC or use the Visual Studio x64 Developer PowerShell."
}
Write-Host "Built native/win-x64/matchbonus.dll"
