<#
.SYNOPSIS
Enforces the command SCHEMA contract — what a caller (an MCP agent, the Vue frontend, a future
pipeline edge) can know about a command without reading its source. Fails (exit 1) on violation.

The contract:

    1. Every command carries a Description        it is what drives command selection, not the schema
    2. A command that reads ctx.Payload           declares InputType — otherwise the agent guesses
    3. Every property of a declared InputType     carries [Description] — a field name is not a spec
    4. Every command in AnalyseTool.Tools         declares OutputType

Rule 4 is deliberately scoped to Tools. The Core commands are extension management and scripting:
they never appear in a pipeline, most are HiddenFromMcp, and a declared result buys them nothing
(the decision is recorded in commit 5a3856a). Tools is where the feature commands live, and where a
missing schema is a caller reading prose to guess a shape.

Rule 3 checks ONE level — the declared input type's own properties, not the types they nest. The
top level is what an agent reads first; recursing means chasing every DTO in the repository for a
diminishing return. Say so rather than implying full coverage.

Known limits, stated rather than discovered later:
  - This is a source scan, not a compiler. It reads what is written, and a command assembled at
    runtime is invisible to it.
  - Files that GENERATE command source (the scripting templates) are skipped by path — a generator
    emitting [RevitCommand] is not itself a command. Every other line-start [RevitCommand] must
    parse, and one that does not FAILS the run instead of being quietly skipped.

Run locally:  pwsh -File src/build/Check-Schemas.ps1
CI runs it through the Nuke `CheckSchemas` target, before any build, so it fails fast.
#>

$ErrorActionPreference = 'Stop'
$src = Split-Path $PSScriptRoot -Parent
$failures = @()

# Source generators, not commands: these emit [RevitCommand] into a string for a script extension.
$templateFiles = @(
    'Features/Scripting/SaveAsCommand.cs',
    'Common/Extensions/Scripting/RoslynScriptCompiler.cs',
    'Features/Extensions/Templates/'
)

# [RevitCommand ... )] immediately followed by the class it decorates.
#   group 1 = the whole argument list including brackets   group 2 = the arguments   group 3 = class
$commandPattern = '^[ \t]*\[RevitCommand(\]|\((.*?)\)\])[\s\S]*?\b(?:internal|public)\s+(?:sealed\s+)?class\s+(\w+)'
$occurrencePattern = '^[ \t]*\[RevitCommand'
$options = [System.Text.RegularExpressions.RegexOptions]::Multiline -bor `
           [System.Text.RegularExpressions.RegexOptions]::Singleline

function Get-SourceFiles($root) {
    Get-ChildItem $root -Recurse -Include *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
}

# ---- 1. Collect every declared command ---------------------------------------------------------
$commands = @()
foreach ($project in 'AnalyseTool.Tools', 'AnalyseTool.Core') {
    foreach ($file in Get-SourceFiles (Join-Path $src $project)) {
        $relative = $file.FullName.Substring($src.Length + 1).Replace('\', '/')
        $text = Get-Content $file.FullName -Raw
        if (-not $text) { continue }

        $occurrences = [regex]::Matches($text, $occurrencePattern, $options).Count
        if ($occurrences -eq 0) { continue }
        if ($templateFiles | Where-Object { $relative -like "*$_*" }) { continue }

        $matched = @([regex]::Matches($text, $commandPattern, $options))
        if ($matched.Count -ne $occurrences) {
            # Not a pass: an occurrence this script cannot read is an unchecked command.
            $failures += "UNPARSED: $relative has $occurrences [RevitCommand] occurrences but $($matched.Count) parsed"
            continue
        }

        for ($i = 0; $i -lt $matched.Count; $i++) {
            $match = $matched[$i]
            # A command's body runs until the next command in the same file (several share one file).
            $bodyEnd = if ($i + 1 -lt $matched.Count) { $matched[$i + 1].Index } else { $text.Length }
            $commands += [pscustomobject]@{
                Project = $project
                File    = $relative
                Class   = $match.Groups[3].Value
                Args    = $match.Groups[2].Value
                Body    = $text.Substring($match.Index + $match.Length, $bodyEnd - $match.Index - $match.Length)
                Line    = ($text.Substring(0, $match.Index) -split "`n").Count
            }
        }
    }
}

if ($commands.Count -eq 0) { $failures += "NO COMMANDS FOUND: the scan matched nothing, which means it is broken" }

# ---- 2. Index every type declaration so an InputType can be located ----------------------------
# Keyed by the FORWARD-SLASHED path relative to src/, the same shape a command records, so a lookup
# never depends on which separator the host uses.
$allSources = @{}
foreach ($file in Get-SourceFiles $src) {
    $key = $file.FullName.Substring($src.Length + 1).Replace('\', '/')
    $allSources[$key] = Get-Content $file.FullName -Raw
}

# Resolves ONE type name inside a scope (a whole file, or an enclosing type's body).
# Returns the type's body, '' for a positional record that has none, or $null when absent.
function Get-TypeBodyInScope([string] $scope, [string] $typeName) {
    $declaration = '\b(?:class|record|struct)\s+' + [regex]::Escape($typeName) + '\b'
    $match = [regex]::Match($scope, $declaration)
    if (-not $match.Success) { return $null }

    $open = $scope.IndexOf('{', $match.Index + $match.Length)
    $semicolon = $scope.IndexOf(';', $match.Index + $match.Length)
    # A positional record with no body: nothing to check, but the type WAS found.
    if ($open -lt 0 -or ($semicolon -ge 0 -and $semicolon -lt $open)) { return '' }

    $depth = 0
    for ($i = $open; $i -lt $scope.Length; $i++) {
        if ($scope[$i] -eq '{') { $depth++ }
        elseif ($scope[$i] -eq '}') { $depth--; if ($depth -eq 0) { return $scope.Substring($open, $i - $open) } }
    }
    return ''
}

# Walks a dotted name (Outer.Inner) segment by segment, narrowing the scope each time.
function Get-NestedTypeBody([string] $scope, [string[]] $segments) {
    $body = $scope
    foreach ($segment in $segments) {
        $body = Get-TypeBodyInScope $body $segment
        if ($null -eq $body) { return $null }
    }
    return $body
}

# Most InputTypes are a nested `Request`/`Payload`, and DOZENS of commands nest a type by that same
# short name. Resolving on the short name alone picks an arbitrary one — worse, an arbitrary one per
# run, since hashtable order is not stable. So: the command's OWN file first, and only then the rest
# of the tree, where more than one answer is reported as ambiguous instead of silently chosen.
function Resolve-InputTypeBody([string] $fullName, [string] $commandFile) {
    $segments = $fullName -split '\.'
    if ($allSources.ContainsKey($commandFile)) {
        $body = Get-NestedTypeBody $allSources[$commandFile] $segments
        if ($null -ne $body) { return @{ Body = $body; Count = 1 } }
    }

    $found = @()
    foreach ($text in $allSources.Values) {
        if (-not $text) { continue }
        $body = Get-NestedTypeBody $text $segments
        if ($null -ne $body) { $found += , $body }
    }
    if ($found.Count -eq 0) { return @{ Body = $null; Count = 0 } }
    return @{ Body = $found[0]; Count = $found.Count }
}

# Only the attribute's OPENING is matched, never its full extent: a description may itself contain
# ']' (e.g. "Returns { edits: [...] }"), and a pattern trying to span the whole attribute would stop
# there and report a property that is in fact documented.
$propertyPattern = 'public\s+[\w<>\?\[\],\. ]+?\s+(\w+)\s*\{\s*get'
$descriptionPattern = '\[\s*(?:System\.ComponentModel\.)?Description\s*\('

# ---- 3. The four rules -------------------------------------------------------------------------
foreach ($command in $commands) {
    $where = "$($command.File):$($command.Line) $($command.Class)"

    if ($command.Args -cnotmatch 'Description\s*=\s*"') {
        $failures += "DESCRIPTION: $where declares no Description (it is what an agent picks a command by)"
    }

    $inputType = [regex]::Match($command.Args, 'InputType\s*=\s*typeof\(([\w\.]+)\)')
    if (-not $inputType.Success) {
        if ($command.Body -cmatch '\bPayload\b') {
            $failures += "INPUT SCHEMA: $where reads ctx.Payload but declares no InputType"
        }
    }
    else {
        $typeName = $inputType.Groups[1].Value
        $resolved = Resolve-InputTypeBody $typeName $command.File
        if ($resolved.Count -eq 0) {
            $failures += "INPUT SCHEMA: $where declares InputType $typeName, which was not found in src/"
        }
        elseif ($resolved.Count -gt 1) {
            $failures += "INPUT SCHEMA: $where declares InputType $typeName, which matches $($resolved.Count) types in src/ — qualify it (Outer.Inner) so this check reads the one you meant"
        }
        else {
            $body = $resolved.Body
            $previous = 0
            foreach ($property in [regex]::Matches($body, $propertyPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)) {
                $preceding = $body.Substring($previous, $property.Index - $previous)
                if ($preceding -cnotmatch $descriptionPattern) {
                    $failures += "INPUT FIELD: $typeName.$($property.Groups[1].Value) has no [Description] (used by $where)"
                }
                $previous = $property.Index + $property.Length
            }
        }
    }

    if ($command.Project -eq 'AnalyseTool.Tools' -and $command.Args -cnotmatch 'OutputType\s*=\s*typeof') {
        $failures += "OUTPUT SCHEMA: $where declares no OutputType"
    }
}

# ---- Report ------------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host "Command schema contract VIOLATED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Command schema contract OK: $($commands.Count) commands — all describe themselves, all payload readers declare InputType with described fields, all Tools commands declare OutputType." -ForegroundColor Green
exit 0
