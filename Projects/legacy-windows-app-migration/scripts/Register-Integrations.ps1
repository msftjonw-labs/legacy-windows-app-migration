param([switch]$Unregister, [switch]$ShellMenu)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root 'bin\LegacyOrders.Pricing.dll'
$exe = Join-Path $root 'bin\LegacyOrders.exe'
$clsid = '{8FDE9697-E5CD-4747-A0DC-67770582AC6C}'
$classPath = "Software\Classes\CLSID\$clsid"
$progIdPath = 'Software\Classes\LegacyOrders.PricingEngine'
$menuPath = 'Software\Classes\SystemFileAssociations\.csv\shell\LegacyOrdersImport'
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry32)
try {
    if ($Unregister) {
        foreach ($path in @($classPath, $progIdPath)) { $registry.DeleteSubKeyTree($path, $false) }
        if ($ShellMenu) {
            $nativeRegistry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
            try { $nativeRegistry.DeleteSubKeyTree($menuPath, $false) } finally { $nativeRegistry.Dispose() }
        }
        Write-Host 'Removed per-user COM registration. Shell menu removed only when -ShellMenu is specified.'
        return
    }
    if (!(Test-Path $dll) -or !(Test-Path $exe)) { throw 'Run .\build.ps1 first.' }
    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($dll).FullName
    $codeBase = ([Uri]$dll).AbsoluteUri
    $class = $registry.CreateSubKey($classPath)
    try {
        $class.SetValue('', 'Legacy Orders Pricing Engine')
        $progId = $class.CreateSubKey('ProgId')
        try { $progId.SetValue('', 'LegacyOrders.PricingEngine') } finally { $progId.Dispose() }
        $server = $class.CreateSubKey('InprocServer32')
        try {
            $server.SetValue('', 'mscoree.dll')
            $server.SetValue('ThreadingModel', 'Both')
            $server.SetValue('Class', 'LegacyOrders.Pricing.PricingEngine')
            $server.SetValue('Assembly', $assemblyName)
            $server.SetValue('RuntimeVersion', 'v4.0.30319')
            $server.SetValue('CodeBase', $codeBase)
            $version = $server.CreateSubKey('1.0.0.0')
            try {
                $version.SetValue('Class', 'LegacyOrders.Pricing.PricingEngine')
                $version.SetValue('Assembly', $assemblyName)
                $version.SetValue('RuntimeVersion', 'v4.0.30319')
                $version.SetValue('CodeBase', $codeBase)
            } finally { $version.Dispose() }
        } finally { $server.Dispose() }
    } finally { $class.Dispose() }
    $progId = $registry.CreateSubKey($progIdPath)
    try {
        $key = $progId.CreateSubKey('CLSID')
        try { $key.SetValue('', $clsid) } finally { $key.Dispose() }
    } finally { $progId.Dispose() }
    if ($ShellMenu) {
        $nativeRegistry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
        try {
            $menu = $nativeRegistry.CreateSubKey($menuPath)
            try {
                $menu.SetValue('', 'Import into Legacy Orders')
                $command = $menu.CreateSubKey('command')
                try { $command.SetValue('', "`"$exe`" --import `"%1`"") } finally { $command.Dispose() }
            } finally { $menu.Dispose() }
        } finally { $nativeRegistry.Dispose() }
    }
    Write-Host 'Registered x86 COM pricing for the current user. No administrator rights required.'
} finally { $registry.Dispose() }
