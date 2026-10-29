param(
    [Parameter(Mandatory)][string]$SnapshotPath,
    [string]$OutputPath = 'src/Presentation.Telegram/appsettings.Local.json',
    [Parameter(Mandatory)][string]$PreservedSecretsPath,
    [switch]$EnableAllEvmChains
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ConfigurationSecrets.ps1')
# Accept docker inspect JSON, an object containing Config.Env, or a raw Config.Env array.
$snapshot = Get-Content -LiteralPath $SnapshotPath -Raw | ConvertFrom-Json -AsHashtable
if ($snapshot -is [array] -and $snapshot.Count -eq 1 -and $snapshot[0] -is [System.Collections.IDictionary]) { $snapshot = $snapshot[0] }
$environment = if ($snapshot -is [System.Collections.IDictionary]) { $snapshot['Config']['Env'] } else { $snapshot }
if (-not $environment) { throw 'Snapshot has no Config.Env entries.' }
$settings = @{}
$credentials = @{}
foreach ($entry in $environment) {
    $parts = $entry -split '=', 2
    if ($parts.Count -ne 2) { continue }
    $key = $parts[0].Replace('__', ':')
    $value = $parts[1]
    if (Test-RetiredCredentialKey $key) { continue }
    if (($key -match '(Url|Uri)$' -or (Test-CredentialKey $key)) -and [string]::IsNullOrWhiteSpace($value)) { continue }
    if (Test-CredentialKey $key) { $credentials[$key] = $value; continue }
    # Preserve endpoint ownership even for public URLs: stale secret-source URLs must not replace them.
    # The exporter emits only confidential endpoints from this private preservation map.
    if ($key -match '(Url|Uri)$') { $credentials[$key] = $value }
    if (Test-ConfidentialEndpoint $key $value) { $credentials[$key] = $value; continue }
    if ($key -match '^(Billing|BlockchainNetworks|Hyperliquid|Polymarket|PolymarketPolygon|EnsReverseLookup|AssetPrices|Serilog):') {
        $settings[$key] = $value
    }
}
if (-not $credentials['ConnectionStrings:Default']) { throw 'Snapshot must preserve the running database connection.' }
$chains = @('Bsc', 'Base', 'Arbitrum', 'Ethereum', 'Robinhood')
for ($i = 0; $i -lt $chains.Count; $i++) {
    $key = "BlockchainNetworks:Networks:$i`:Chain"
    if ($settings.ContainsKey($key) -and $settings[$key] -ne $chains[$i]) { throw 'Snapshot network order differs from the supported chain order; review manually.' }
    $settings[$key] = $chains[$i]
    if ($EnableAllEvmChains) { $settings["BlockchainNetworks:Networks:$i`:Enabled"] = 'true' }
}
# Flat colon-delimited keys are native .NET configuration keys, including array indices.
$settings | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath
$credentials | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $PreservedSecretsPath
Write-Host 'Migrated private settings and preserved credentials without displaying values.'
