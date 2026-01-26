param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dist = $PSScriptRoot
$backendRoot = Join-Path $root "..\CitadelX.Backend"
$backendPackages = Join-Path $backendRoot "modules\packages"
$backendModules = Join-Path $backendRoot "modules"
if (Test-Path $backendRoot) {
    New-Item -ItemType Directory -Force -Path $backendPackages | Out-Null
    New-Item -ItemType Directory -Force -Path $backendModules | Out-Null
}

$modules = @(
    @{
        Name = "Singbox"
        BackendProject = "SingboxModule\CitadelX.SingboxModule.csproj"
        BackendDll = "CitadelX.SingboxModule.dll"
        NodeProject = "SingboxNodeModule\SingboxNodeModule.csproj"
        NodeDll = "CitadelX.SingboxNodeModule.dll"
    },
    @{
        Name = "SingboxExtended"
        BackendProject = "SingboxExtendedModule\CitadelX.SingboxExtendedModule.csproj"
        BackendDll = "CitadelX.SingboxExtendedModule.dll"
        NodeProject = "SingboxExtendedNodeModule\SingboxExtendedNodeModule.csproj"
        NodeDll = "CitadelX.SingboxExtendedNodeModule.dll"
    }
)

foreach ($module in $modules) {
    Write-Host "Building $($module.Name) (Backend)..."
    dotnet build (Join-Path $root $module.BackendProject) -c $Configuration

    Write-Host "Building $($module.Name) (Node)..."
    dotnet build (Join-Path $root $module.NodeProject) -c $Configuration

    $backendOut = Join-Path $root ("{0}\bin\{1}\net8.0" -f (Split-Path $module.BackendProject -Parent), $Configuration)
    $nodeOut = Join-Path $root ("{0}\bin\{1}\net8.0" -f (Split-Path $module.NodeProject -Parent), $Configuration)

    $backendDllPath = Join-Path $backendOut $module.BackendDll
    $nodeDllPath = Join-Path $nodeOut $module.NodeDll

    if (!(Test-Path $backendDllPath)) {
        throw "Backend DLL not found: $backendDllPath"
    }
    if (!(Test-Path $nodeDllPath)) {
        throw "Node DLL not found: $nodeDllPath"
    }

    $zipPath = Join-Path $dist ("{0}.zip" -f $module.Name)
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path @($backendDllPath, $nodeDllPath) -DestinationPath $zipPath -Force
    Write-Host "Packaged: $zipPath"

    if (Test-Path $backendRoot) {
        $backendCopy = Join-Path $backendPackages (Split-Path $zipPath -Leaf)
        Copy-Item $zipPath $backendCopy -Force
        Write-Host "Copied to backend packages: $backendCopy"

        $backendModuleCopy = Join-Path $backendModules $module.BackendDll
        Copy-Item $backendDllPath $backendModuleCopy -Force
        Write-Host "Copied backend module DLL: $backendModuleCopy"
    }
}
