# The headers of the AnalyseTool MCP server for Claude Code (headersHelper of .mcp.json): the token AnalyseTool keeps in
# %LOCALAPPDATA%\AnalyseTool\mcp.json — one for every Revit version, written the first time AnalyseTool's MCP server starts.
$ErrorActionPreference = 'Stop'
$file = Join-Path $env:LOCALAPPDATA 'AnalyseTool\mcp.json'
if (-not (Test-Path $file)) {
    [Console]::Error.WriteLine("AnalyseTool has not started its MCP server on this computer yet: open AnalyseTool once in Revit (and turn on AI clients in its profile). No $file.")
    exit 1
}

$token = (Get-Content -Raw -LiteralPath $file | ConvertFrom-Json).token
if ([string]::IsNullOrWhiteSpace($token)) {
    [Console]::Error.WriteLine("$file has no token: open AnalyseTool in Revit once, it writes a new one.")
    exit 1
}

@{ Authorization = "Bearer $token" } | ConvertTo-Json -Compress
