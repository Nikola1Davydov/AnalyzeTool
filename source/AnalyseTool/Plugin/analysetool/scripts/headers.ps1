$ProgressPreference = 'SilentlyContinue'
$f = Join-Path $env:LOCALAPPDATA 'AnalyseTool\mcp.json'
if (-not (Test-Path $f)) { [Console]::Error.WriteLine('AnalyseTool has not started its MCP server yet: open AnalyseTool once in Revit. No ' + $f); exit 1 }
$t = (Get-Content -Raw -LiteralPath $f | ConvertFrom-Json).token
if (-not $t) { [Console]::Error.WriteLine('No token in ' + $f + ': open AnalyseTool in Revit once.'); exit 1 }
[Console]::Out.Write((@{ Authorization = 'Bearer ' + $t } | ConvertTo-Json -Compress))
