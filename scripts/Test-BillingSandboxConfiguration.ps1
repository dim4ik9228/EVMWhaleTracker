$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scratch = Join-Path $repoRoot ('artifacts/sandbox-config-fixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$fixture = Join-Path $scratch 'fixture.json'
$output = Join-Path $scratch '.env.billing-sandbox'
@{
    TelegramBot = @{ BotToken = 'fixture-token' }
    BillingSandbox = @{ NowPaymentsApiKey = 'fixture-sandbox-api'; NowPaymentsIpnSecret = 'fixture-sandbox-ipn' }
    Billing = @{ NowPaymentsApiKey = 'must-not-export-production-api'; NowPaymentsIpnSecret = 'must-not-export-production-ipn' }
    ConnectionStrings = @{ Default = 'Host=production;Database=Production;Password=must-not-export-db' }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture
& (Join-Path $PSScriptRoot 'Export-BillingSandboxEnv.ps1') -SecretsPath $fixture -OutputPath $output -DatabasePassword 'fixture-only-password'
$envText = Get-Content -LiteralPath $output -Raw
if ($envText -match 'must-not-export|BillingSandbox__|SandboxMode') { throw 'Export leaked production configuration or ordinary switches.' }
foreach ($required in @('fixture-sandbox-api', 'fixture-sandbox-ipn', 'fixture-token', 'EvmWhaleTrackerBillingSandbox', 'billing-sandbox-db')) {
    if (-not $envText.Contains($required)) { throw 'Expected isolated fixture setting absent.' }
}
try {
    & (Join-Path $PSScriptRoot 'Export-BillingSandboxEnv.ps1') -SecretsPath $fixture -OutputPath (Join-Path $scratch '.env')
    throw 'Exporter accepted a production-style output.'
} catch { if ($_.Exception.Message -eq 'Exporter accepted a production-style output.') { throw } }
# Compose validation uses only copied declarations and fixture credentials, never the deployment env.
Copy-Item -LiteralPath (Join-Path $repoRoot 'docker-compose.billing-sandbox.yml') -Destination $scratch
& docker compose --project-directory $scratch --env-file $output -f (Join-Path $scratch 'docker-compose.billing-sandbox.yml') config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Sandbox Compose validation failed.' }
Write-Host 'Sandbox synthetic export and Compose checks passed; fixture artifacts retained under artifacts/.'
