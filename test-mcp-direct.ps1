# Test direct Serena MCP call via docker exec
$initRequest = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}'
$toolRequest = '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"activate_project","arguments":{"project":"D:\\SITES\\My\\Codedev"}}}'

$input = "$initRequest`n$toolRequest"

Write-Host "Sending requests to Serena MCP..."

# Use UTF8 without BOM
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$inputBytes = $utf8NoBom.GetBytes($input)

# Write to temp file and pipe it
$tempFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllBytes($tempFile, $inputBytes)

$output = Get-Content $tempFile -Raw | docker exec -i serena-mcp /workspaces/serena/.venv/bin/serena-mcp-server

Remove-Item $tempFile

Write-Host "`n=== FULL OUTPUT ==="
Write-Host $output

Write-Host "`n=== SEARCH JSON RESPONSES ==="
$lines = $output -split "`n"
foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith('{"jsonrpc"') -and $trimmed.Contains('"id":2')) {
        Write-Host "Found tool response:"
        Write-Host $trimmed
    }
}
