param(
    [Parameter(Mandatory = $true)]
    [string]$BotToken
)

$baseUrl = "https://api.telegram.org/bot$BotToken"

Write-Host "Checking bot profile..."
Invoke-RestMethod -Method Get -Uri "$baseUrl/getMe"

Write-Host "`nRecent updates (send the bot a message first if this is empty)..."
Invoke-RestMethod -Method Get -Uri "$baseUrl/getUpdates?timeout=1"
