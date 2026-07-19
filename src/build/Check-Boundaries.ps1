<#
.SYNOPSIS
Enforces the dependency contract of the AnalyseTool platform split. Fails (exit 1) on violation.

The contract (see also src/LLM.md):

    Extensions  -> Sdk                      (never checked here; they live outside the repo)
    Tools       -> Sdk                      feature commands ride the public contract ONLY
    Core        -> Sdk                      the platform
    Mcp.Bridge  -> Core + Sdk               transport satellite
    App         -> Core + Tools + Mcp.Bridge + Sdk
    Sdk         -> (nothing)

Also enforced:
    - Core and Tools stay HEADLESS: no UseWPF/UseWindowsForms, no System.Windows usage in code.

Run locally:  powershell -File src/build/Check-Boundaries.ps1
CI runs it before any build so violations fail fast.
#>

$ErrorActionPreference = 'Stop'
$src = Split-Path $PSScriptRoot -Parent
$failures = @()

# ---- 1. ProjectReference whitelist per project -------------------------------------------------
$contract = @{
    'AnalyseTool.Sdk'        = @()
    'AnalyseTool.Core'       = @('AnalyseTool.Sdk')
    'AnalyseTool.Tools'      = @('AnalyseTool.Sdk')
    'AnalyseTool.Mcp.Bridge' = @('AnalyseTool.Core', 'AnalyseTool.Sdk')
    'AnalyseTool.App'        = @('AnalyseTool.Core', 'AnalyseTool.Mcp.Bridge', 'AnalyseTool.Sdk', 'AnalyseTool.Tools')
    'AnalyseTool.Mcp'        = @()
}

foreach ($name in $contract.Keys) {
    $csproj = Join-Path $src "$name\$name.csproj"
    if (-not (Test-Path $csproj)) { $failures += "MISSING PROJECT: $csproj"; continue }

    [xml]$xml = Get-Content $csproj
    $refs = @($xml.SelectNodes('//ProjectReference') | ForEach-Object {
        [System.IO.Path]::GetFileNameWithoutExtension($_.Include)
    })

    foreach ($ref in $refs) {
        if ($contract[$name] -notcontains $ref) {
            $failures += "BOUNDARY: $name references $ref (allowed: $($contract[$name] -join ', '))"
        }
    }
}

# ---- 2. Headless invariant for Core and Tools --------------------------------------------------
foreach ($name in 'AnalyseTool.Core', 'AnalyseTool.Tools') {
    $csproj = Join-Path $src "$name\$name.csproj"
    $raw = Get-Content $csproj -Raw
    if ($raw -match '<UseWPF>\s*true|<UseWindowsForms>\s*true') {
        $failures += "HEADLESS: $name declares a UI framework (UseWPF/UseWindowsForms)"
    }

    Get-ChildItem (Join-Path $src $name) -Recurse -Include *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        ForEach-Object {
            if (Select-String -Path $_.FullName -Pattern '^\s*using\s+System\.Windows|MessageBox\.Show' -Quiet) {
                $failures += "HEADLESS: $name uses System.Windows / MessageBox in $($_.Name)"
            }
        }
}

# ---- Report ------------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host "Dependency contract VIOLATED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Dependency contract OK: Tools->Sdk, Core->Sdk, Mcp.Bridge->Core+Sdk, App->Core+Tools+Mcp.Bridge+Sdk; Core/Tools headless." -ForegroundColor Green
exit 0
