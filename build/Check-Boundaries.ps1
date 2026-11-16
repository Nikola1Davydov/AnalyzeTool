#Requires -Version 7
<#
.SYNOPSIS
    Checks the project boundaries: who may reference whom, and the rules of the add-in code below.

.DESCRIPTION
    - References: every project in source/ may reference only the projects and packages of its row in $Rules;
      a new project fails until it gets a row here.
    - AnalyseTool.Hosting, .Mcp, .Scripting and .Visualization are headless (Headless in $Rules): no UseWPF /
      UseWindowsForms, no System.Windows, no MessageBox.
    - AnalyseTool: Features/ and Services/ use no System.Windows (UI goes through IUserNotifier and the
      frontend) and no ActiveUIDocument (the document goes through ActiveDocument). Exceptions:
      Services/Documents/ActiveDocument.cs and a line marked "// AllowActiveDocument: <reason>".
    - AnalyseTool.Launcher uses no types of the add-in or the modules: they would load into Revit's
      context and bypass the isolation. Names in strings, as in PluginInfo, are fine.

    Prints every violation as "file:line: rule" and exits with 1 when there is any.

.EXAMPLE
    pwsh build/Check-Boundaries.ps1
#>
param(
    [string] $Root = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'

# What each project may reference. Packages are wildcard patterns.
$Common = @('JetBrains.Annotations', 'Polyfill')
$RevitApi = @('Nice3point.Revit.Api.*')
$Rules = [ordered]@{
    'AnalyseTool.Hosting' = @{
        Projects = @()
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Microsoft.Extensions.*')
        Headless = $true
    }
    'AnalyseTool.Hosting.WebView' = @{
        Projects = @('AnalyseTool.Hosting')
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Microsoft.Web.WebView2', 'Microsoft.Extensions.*')
    }
    'AnalyseTool.Hosting.Mcp' = @{
        Projects = @('AnalyseTool.Hosting')
        Packages = $RevitApi + $Common + @('ModelContextProtocol.Core', 'Microsoft.Extensions.*')
        Headless = $true
    }
    'AnalyseTool.Hosting.Scripting' = @{
        Projects = @('AnalyseTool.Hosting', 'AnalyseTool.Hosting.Mcp')
        Packages = $RevitApi + $Common + @('Microsoft.CodeAnalysis.*', 'Microsoft.Extensions.*')
        Headless = $true
    }
    'AnalyseTool.Hosting.Visualization' = @{
        Projects = @()
        Packages = $RevitApi + $Common + @('Microsoft.Extensions.*')
        Headless = $true
    }
    'AnalyseTool.Sdk' = @{
        Projects = @()
        Packages = $RevitApi + @('Newtonsoft.Json')
        Headless = $true
    }
    'AnalyseTool.Extensions' = @{
        Projects = @('AnalyseTool.Hosting', 'AnalyseTool.Hosting.Mcp', 'AnalyseTool.Sdk')
        Packages = $RevitApi + $Common + @('Microsoft.CodeAnalysis.*', 'Microsoft.Extensions.*', 'LibGit2Sharp', 'Newtonsoft.Json')
        Headless = $true
    }
    'AnalyseTool.ServiceDefaults' = @{
        Projects = @('AnalyseTool.Hosting')
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Nice3point.Revit.Logging', 'Microsoft.Extensions.*', 'Serilog.*')
    }
    'AnalyseTool.Launcher' = @{
        Projects = @('AnalyseTool')
        Packages = $RevitApi
    }
    'AnalyseTool' = @{
        Projects = @('AnalyseTool.ServiceDefaults', 'AnalyseTool.Hosting', 'AnalyseTool.Hosting.WebView', 'AnalyseTool.Hosting.Mcp', 'AnalyseTool.Hosting.Scripting', 'AnalyseTool.Hosting.Visualization', 'AnalyseTool.Extensions', 'AnalyseTool.Sdk')
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Nice3point.Revit.Extensions', 'Microsoft.Extensions.*')
    }
}

$violations = [System.Collections.Generic.List[string]]::new()

function Add-Violation([string] $Path, [int] $Line, [string] $Message) {
    $relative = [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
    $violations.Add("${relative}:${Line}: $Message")
}

# Code of a line without comments and string literals, so names in them do not count
function Get-Code([string] $Line) {
    $code = $Line -replace '@?"(?:[^"\\]|\\.)*"', '""'
    $comment = $code.IndexOf('//')
    if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
    return $code
}

# Line-by-line check of the C# files under $Folder, skipping bin/obj
function Test-Code([string] $Folder, [string] $Pattern, [string] $Message, [scriptblock] $Allow = { $false }) {
    if (-not (Test-Path $Folder)) { return }

    Get-ChildItem $Folder -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $file = $_
            $number = 0
            foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
                $number++
                if ((Get-Code $line) -match $Pattern -and -not (& $Allow $file $line)) {
                    Add-Violation $file.FullName $number $Message
                }
            }
        }
}

# References
$projects = Get-ChildItem (Join-Path $Root 'source') -Recurse -Filter *.csproj -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' }

foreach ($project in $projects) {
    $name = $project.BaseName
    if (-not $Rules.Contains($name)) {
        Add-Violation $project.FullName 1 "the project has no row in build/Check-Boundaries.ps1"
        continue
    }

    $rule = $Rules[$name]
    [xml] $xml = Get-Content $project.FullName -Raw

    foreach ($reference in $xml.SelectNodes('//ProjectReference')) {
        $target = [System.IO.Path]::GetFileNameWithoutExtension($reference.Include)
        if ($target -notin $rule.Projects) {
            Add-Violation $project.FullName 1 "$name must not reference the project $target"
        }
    }

    foreach ($reference in $xml.SelectNodes('//PackageReference')) {
        $package = $reference.Include
        if (-not ($rule.Packages | Where-Object { $package -like $_ })) {
            Add-Violation $project.FullName 1 "$name must not reference the package $package"
        }
    }

    if ($rule.Headless) {
        foreach ($property in 'UseWPF', 'UseWindowsForms') {
            if ($xml.SelectSingleNode("//$property")?.InnerText -eq 'true') {
                Add-Violation $project.FullName 1 "$name is headless: no $property"
            }
        }
    }
}

# Code
$source = Join-Path $Root 'source'
$noWpf = '\bSystem\.Windows\b|\bMessageBox\b'

foreach ($name in $Rules.Keys | Where-Object { $Rules[$_].Headless }) {
    Test-Code (Join-Path $source $name) $noWpf `
        "$name is headless: no System.Windows or MessageBox"
}

foreach ($folder in 'Features', 'Services') {
    $path = Join-Path $source "AnalyseTool/$folder"

    Test-Code $path $noWpf `
        "no System.Windows in $folder/: UI goes through IUserNotifier and the frontend"

    Test-Code $path '\bActiveUIDocument\b' `
        "no ActiveUIDocument in ${folder}/: take the document from ActiveDocument, or mark the line with // AllowActiveDocument: <reason>" `
        { param($file, $line) $file.Name -eq 'ActiveDocument.cs' -or $line -match '//\s*AllowActiveDocument:\s*\S' }
}

# Plain names, not a hand-escaped regex: dotnet new renames the add-in name only where it is written as is
$addinName = 'AnalyseTool'
$modulesName = 'AnalyseTool.Hosting'
$foreignTypes = "\b(?:$([regex]::Escape($modulesName))|$([regex]::Escape($addinName))(?!\.Launcher\b))\b"

Test-Code (Join-Path $source "$addinName.Launcher") $foreignTypes `
    'the Launcher must not use types of the add-in or the modules: they would load outside its AssemblyLoadContext and bypass the isolation'

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host $_ }
    Write-Host "Project boundaries: $($violations.Count) violation(s)"
    exit 1
}

Write-Host "Project boundaries: OK ($($projects.Count) projects)"
