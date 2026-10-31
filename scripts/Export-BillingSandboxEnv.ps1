param(
    [string]$SecretsPath,
    [string]$OutputPath = '.env.billing-sandbox',
    [string]$DatabasePassword
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ConfigurationSecrets.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SecretsPath) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/Presentation.Telegram/Presentation.Telegram.csproj')
    $id = @($project.Project.PropertyGroup.UserSecretsId | Where-Object { $_ })[0]
    $SecretsPath = Join-Path $env:APPDATA "Microsoft/UserSecrets/$id/secrets.json"
}
$output = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }))
if ([IO.Path]::GetFileName($output) -ne '.env.billing-sandbox') { throw 'Only a dedicated .env.billing-sandbox output is permitted.' }
$source = Get-FlatConfiguration (Get-Content -LiteralPath $SecretsPath -Raw | ConvertFrom-Json -AsHashtable)
foreach ($key in @('TelegramBot:BotToken', 'BillingSandbox:NowPaymentsApiKey', 'BillingSandbox:NowPaymentsIpnSecret')) {
    if ([string]::IsNullOrWhiteSpace($source[$key])) { throw 'Required sandbox credential missing.' }
}
if (-not $DatabasePassword) { $DatabasePassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
# The connection is constructed from scratch: production DB credentials and merchant settings are never copied.
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Host'] = 'billing-sandbox-db'; $connection['Port'] = '5432'
$connection['Database'] = 'EvmWhaleTrackerBillingSandbox'; $connection['Username'] = 'postgres'; $connection['Password'] = $DatabasePassword
$values = @{
    'TelegramBot__BotToken' = $source['TelegramBot:BotToken']
    'Billing__NowPaymentsApiKey' = $source['BillingSandbox:NowPaymentsApiKey']
    'Billing__NowPaymentsIpnSecret' = $source['BillingSandbox:NowPaymentsIpnSecret']
    'ConnectionStrings__Default' = $connection.ConnectionString
    'BILLING_SANDBOX_DB_PASSWORD' = $DatabasePassword
}
$lines = foreach ($key in ($values.Keys | Sort-Object)) { ConvertTo-DotEnvLine $key ([string]$values[$key]) }
if (Test-Path -LiteralPath $output) { throw 'Sandbox env already exists; preserve its database password or archive it explicitly before exporting again.' }
[IO.File]::WriteAllLines($output, [string[]]$lines, [Text.UTF8Encoding]::new($false))
Write-Host 'Wrote isolated sandbox credentials; no values displayed.'
