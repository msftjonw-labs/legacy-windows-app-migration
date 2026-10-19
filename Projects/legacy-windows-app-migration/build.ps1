param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) { throw '.NET Framework 4.x compiler is required.' }
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Invoke-Compiler([string[]]$CompilerArguments) {
    & $compiler /nologo /warnaserror+ /platform:x86 @CompilerArguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }
}
$pricing = Join-Path $output 'LegacyOrders.Pricing.dll'
Invoke-Compiler @('/target:library', "/out:$pricing", (Join-Path $PSScriptRoot 'src\Pricing\PricingEngine.cs'))
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src\LegacyOrders') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/define:TRACE', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Runtime.Serialization.dll', '/r:Microsoft.CSharp.dll')
Invoke-Compiler (@('/target:winexe', "/out:$(Join-Path $output 'LegacyOrders.exe')") + $references + $sources)
Copy-Item (Join-Path $PSScriptRoot 'src\LegacyOrders\LegacyOrders.exe.config') (Join-Path $output 'LegacyOrders.exe.config') -Force
if ($Test) {
    Invoke-Compiler (@('/target:exe', "/out:$(Join-Path $output 'LegacyOrders.Tests.exe')", "/r:$pricing", '/main:LegacyOrders.Tests.TestRunner') + $references + $sources + (Join-Path $PSScriptRoot 'tests\TestRunner.cs'))
    & (Join-Path $output 'LegacyOrders.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Host "Built application: $(Join-Path $output 'LegacyOrders.exe')"
