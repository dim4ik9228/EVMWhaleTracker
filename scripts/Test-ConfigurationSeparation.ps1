param([string]$WorkPath = 'artifacts/configuration-tests')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $repo $WorkPath
New-Item -ItemType Directory -Force $work | Out-Null
$secret = 'dollar$ hash# space apostrophe'' double" slash\ end\'
$database = [System.Data.Common.DbConnectionStringBuilder]::new()
$database['Host'] = 'postgres'
$database['Database'] = 'EvmWhaleTracker'
$database['Username'] = 'postgres'
$database['Password'] = $secret
$connection = $database.get_ConnectionString()
$secretsPath = Join-Path $work 'secrets.json'
$snapshotPath = Join-Path $work 'snapshot.json'
$preserved = Join-Path $work 'preserved.json'
$local = Join-Path $work 'appsettings.Local.json'
$output = Join-Path $work '.env'
@{
    'TelegramBot:BotToken' = $secret
    'Billing:NowPaymentsApiKey' = $secret
    'Billing:NowPaymentsIpnSecret' = $secret
    'Billing:EnforcementEnabled' = 'true'
    'Hyperliquid:Enabled' = 'false'
    'BlockchainNetworks:RpcApiKey' = 'shared'
    'BlockchainNetworks:Networks:0:RpcApiKey' = 'specific'
    'BlockchainNetworks:Networks:0:HttpRpcUrl' = 'https://stale.example/private-token'
    'BlockchainNetworks:Networks:1:HttpRpcUrl' = 'https://base-mainnet.infura.io/v3/'
    'PolymarketPolygon:RpcApiKey' = 'polygon'
    'Billing:NowPaymentsEmail' = 'retired@example.invalid'
    'Billing:NowPaymentsPassword' = $secret
    'Billing:NowPaymentsDashboardUrl' = 'https://dashboard.example/?token=retired'
    'BlockchainNetworks' = @{ 'Networks' = @(@{}, @{}, @{ 'RpcApiKey' = '  ' }) }
} | ConvertTo-Json -Depth 30 | Set-Content $secretsPath
@(
    "ConnectionStrings__Default=$connection",
    'Hyperliquid__Enabled=true',
    'Polymarket__Enabled=true',
    'PolymarketPolygon__Enabled=true',
    'Billing__EnforcementEnabled=false',
    'Billing__StarsSalesEnabled=false',
    'Billing__CryptoSalesEnabled=false',
    'Billing__OperatorIds=12345',
    'Billing__NowPaymentsEmail=retired@example.invalid',
    'Billing__NowPaymentsPassword=retired-password',
    'Billing__NowPaymentsDashboardUrl=https://dashboard.example/?token=retired',
    'BlockchainNetworks__Networks__2__WebSocketRpcUrl= ',
    'BlockchainNetworks__Networks__1__HttpRpcUrl=https://rpc.example/private-token',
    'BlockchainNetworks__Networks__0__HttpRpcUrl=https://bsc-mainnet.infura.io/v3/'
) | ConvertTo-Json | Set-Content $snapshotPath
& (Join-Path $PSScriptRoot 'Convert-DockerConfiguration.ps1') -SnapshotPath $snapshotPath -OutputPath $local -PreservedSecretsPath $preserved -EnableAllEvmChains
$settings = Get-Content $local -Raw | ConvertFrom-Json -AsHashtable
if ($settings['Hyperliquid:Enabled'] -ne 'true' -or $settings['Billing:EnforcementEnabled'] -ne 'false') { throw 'Snapshot flags changed.' }
if ($settings.ContainsKey('ConnectionStrings:Default') -or $settings.ContainsKey('BlockchainNetworks:Networks:1:HttpRpcUrl')) { throw 'Credentials leaked to local settings.' }
if (@($settings.Keys | Where-Object { $_ -match 'NowPayments(Email|Password|Dashboard)' }).Count) { throw 'Retired credentials migrated.' }
if ($settings['BlockchainNetworks:Networks:0:HttpRpcUrl'] -ne 'https://bsc-mainnet.infura.io/v3/') { throw 'Captured public endpoint changed.' }
$preservedValues = Get-Content $preserved -Raw | ConvertFrom-Json -AsHashtable
if ($preservedValues['BlockchainNetworks:Networks:0:HttpRpcUrl'] -ne $settings['BlockchainNetworks:Networks:0:HttpRpcUrl']) { throw 'Captured public endpoint ownership lost.' }
for ($i=0; $i -lt 5; $i++) { if ($settings["BlockchainNetworks:Networks:$i`:Enabled"] -ne 'true') { throw 'EVM activation missing.' } }
& (Join-Path $PSScriptRoot 'Export-DockerEnvFromUserSecrets.ps1') -SecretsPath $secretsPath -PreservedSecretsPath $preserved -OutputPath $output
$text = Get-Content $output -Raw
if ($text -match 'Enabled|OperatorIds|NowPayments(Email|Password|Dashboard)|Networks__2__RpcApiKey|Networks__2__WebSocketRpcUrl' -or $text -match 'Networks__1__HttpRpcUrl="https://base-mainnet') { throw 'Nonsecret/stale/empty value exported.' }
if ($text -match 'Networks__0__HttpRpcUrl|stale\.example') { throw 'Stale User Secrets endpoint replaced captured public endpoint.' }
# Resolve Compose in a fixture project so the real .env is never loaded.
Copy-Item (Join-Path $repo 'docker-compose.yml') (Join-Path $work 'compose.yml') -Force
Copy-Item (Join-Path $repo 'docker-compose.debug.yml') (Join-Path $work 'debug.yml') -Force
foreach ($debug in @($false, $true)) {
    $arguments = @('compose', '--project-directory', $work, '--env-file', $output, '-f', (Join-Path $work 'compose.yml'))
    if ($debug) { $arguments += @('-f', (Join-Path $work 'debug.yml')) }
    $arguments += @('config', '--format', 'json')
    $resolved = & docker @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Compose fixture resolution failed.' }
    $config = $resolved | ConvertFrom-Json -AsHashtable
    $envValues = $config.services.app.environment
    if ($config.services.postgres.environment.POSTGRES_PASSWORD.Replace('$$', '$') -cne $secret) { throw 'Database password changed.' }
    foreach ($key in @('TelegramBot__BotToken','Billing__NowPaymentsApiKey','Billing__NowPaymentsIpnSecret')) {
        if ($envValues[$key].Replace('$$', '$') -cne $secret) { throw 'Dotenv quote round-trip failed.' }
    }
    if ($envValues['BlockchainNetworks__Networks__0__RpcApiKey'] -ne 'specific' -or $envValues['BlockchainNetworks__RpcApiKey'] -ne 'shared') { throw 'Per-network key lost.' }
    if ($envValues['BlockchainNetworks__Networks__1__HttpRpcUrl'] -ne 'https://rpc.example/private-token') { throw 'Endpoint credential lost.' }
    if ($envValues.ContainsKey('BlockchainNetworks__Networks__0__HttpRpcUrl')) { throw 'Captured public endpoint was overridden through dotenv.' }
    $expectedEnvironment = if ($debug) { 'Development' } else { 'Production' }
    if ($envValues.ASPNETCORE_ENVIRONMENT -ne $expectedEnvironment -or $envValues.DOTNET_ENVIRONMENT -ne $expectedEnvironment) { throw 'Host environments differ.' }
    $mount = @($config.services.app.volumes | Where-Object { $_.target -eq '/app/appsettings.Local.json' })
    if ($mount.Count -ne 1 -or -not $mount[0].read_only -or $mount[0].bind.create_host_path) { throw 'Private settings mount is not a strict read-only file bind.' }
}
$original = [IO.File]::ReadAllText($output)
$bad = @{ 'TelegramBot:BotToken' = "bad`nvalue" }
$bad | ConvertTo-Json | Set-Content $secretsPath
$rejected = $false
try { & (Join-Path $PSScriptRoot 'Export-DockerEnvFromUserSecrets.ps1') -SecretsPath $secretsPath -PreservedSecretsPath $preserved -OutputPath $output } catch { $rejected = $true }
if (-not $rejected -or [IO.File]::ReadAllText($output) -cne $original) { throw 'Multiline failure replaced dotenv.' }
@{ 'ConnectionStrings:Default' = 'Host=postgres;Password=different' } | ConvertTo-Json | Set-Content $secretsPath
$rejected = $false
try { & (Join-Path $PSScriptRoot 'Export-DockerEnvFromUserSecrets.ps1') -SecretsPath $secretsPath -PreservedSecretsPath $preserved -OutputPath $output } catch { $rejected = $true }
if (-not $rejected -or [IO.File]::ReadAllText($output) -cne $original) { throw 'Conflicting DB connection accepted.' }
Write-Host 'Synthetic migration/export and normal/debug Compose checks passed.'
