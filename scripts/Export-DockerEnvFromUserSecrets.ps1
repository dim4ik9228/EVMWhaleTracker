param(
    [string]$ProjectPath = "src/Presentation.Telegram/Presentation.Telegram.csproj",
    [string]$OutputPath = ".env"
)

$ErrorActionPreference = "Stop"

function Get-UserSecretsId {
    param([string]$CsprojPath)

    [xml]$project = Get-Content -LiteralPath $CsprojPath
    $userSecretsIdNode = $project.Project.PropertyGroup.UserSecretsId | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($userSecretsIdNode)) {
        throw "No <UserSecretsId> element was found in '$CsprojPath'."
    }

    return $userSecretsIdNode.Trim()
}

function Get-SecretValue {
    param(
        [hashtable]$Secrets,
        [string]$Key
    )

    if ($Secrets.ContainsKey($Key)) {
        return [string]$Secrets[$Key]
    }

    return ""
}

function Get-FirstSecretValue {
    param(
        [hashtable]$Secrets,
        [string[]]$Keys
    )

    foreach ($key in $Keys) {
        $value = Get-SecretValue -Secrets $Secrets -Key $key
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return ""
}

function Get-FirstSecretValueOrDefault {
    param(
        [hashtable]$Secrets,
        [string[]]$Keys,
        [string]$DefaultValue
    )

    $value = Get-FirstSecretValue -Secrets $Secrets -Keys $Keys
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFullPath = Join-Path $repoRoot $ProjectPath

if (-not (Test-Path -LiteralPath $projectFullPath)) {
    throw "Project file not found: $projectFullPath"
}

$userSecretsId = Get-UserSecretsId -CsprojPath $projectFullPath
$userSecretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId\secrets.json"

if (-not (Test-Path -LiteralPath $userSecretsPath)) {
    throw "User secrets file not found: $userSecretsPath"
}

$secrets = Get-Content -LiteralPath $userSecretsPath -Raw | ConvertFrom-Json -AsHashtable

$envLines = @(
    "POLYMARKET_POLYGON_FAMILY_0=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:Families:0') -DefaultValue 'ctf-current')",
    "POLYMARKET_POLYGON_FAMILY_1=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:Families:1') -DefaultValue 'ctf-negative-current')",
    "POLYMARKET_POLYGON_FAMILY_2=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:Families:2') -DefaultValue 'position-v2')",
    "POLYMARKET_POLYGON_FAMILY_3=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:Families:3') -DefaultValue 'ctf-legacy')",
    "POLYMARKET_POLYGON_FAMILY_4=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:Families:4') -DefaultValue 'ctf-negative-legacy')",
    "POLYMARKET_POLYGON_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:Enabled') -DefaultValue 'false')",
    "POLYMARKET_POLYGON_HTTP_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:HttpRpcUrl') -DefaultValue 'https://polygon-mainnet.infura.io/v3/')",
    "POLYMARKET_POLYGON_WEB_SOCKET_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:WebSocketRpcUrl') -DefaultValue 'wss://polygon-mainnet.infura.io/ws/v3/')",
    "POLYMARKET_POLYGON_RPC_API_KEY=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:RpcApiKey') -DefaultValue '')",
    "POLYMARKET_POLYGON_REGISTRY_VERSION=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:RegistryVersion') -DefaultValue 'polygon-2026-10-25.1')",
    "POLYMARKET_POLYGON_FINALITY_MODE=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:FinalityMode') -DefaultValue 'Finalized')",
    "POLYMARKET_POLYGON_CONFIRMATION_DEPTH=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:ConfirmationDepth') -DefaultValue '128')",
    "POLYMARKET_POLYGON_MAX_UNIQUE_ADDRESSES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:MaxUniqueAddresses') -DefaultValue '20')",
    "POLYMARKET_POLYGON_MAX_SUBSCRIPTIONS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:MaxSubscriptions') -DefaultValue '6')",
    "POLYMARKET_POLYGON_QUEUE_CAPACITY=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:QueueCapacity') -DefaultValue '256')",
    "POLYMARKET_POLYGON_MAXIMUM_FRAME_BYTES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:MaximumFrameBytes') -DefaultValue '2097152')",
    "POLYMARKET_POLYGON_MAXIMUM_RESPONSE_BYTES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:MaximumResponseBytes') -DefaultValue '4194304')",
    "POLYMARKET_POLYGON_TIMEOUT_SECONDS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:TimeoutSeconds') -DefaultValue '15')",
    "POLYMARKET_POLYGON_RPC_MINIMUM_INTERVAL_MS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:RpcMinimumIntervalMilliseconds') -DefaultValue '250')",
    "POLYMARKET_POLYGON_RECOVERY_BLOCKS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:RecoveryBlocks') -DefaultValue '43200')",
    "POLYMARKET_POLYGON_LOG_RANGE_BLOCKS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:LogRangeBlocks') -DefaultValue '1000')",
    "POLYMARKET_POLYGON_RECOVERY_REQUESTS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:RecoveryRequests') -DefaultValue '200')",
    "POLYMARKET_POLYGON_RETAINED_ANCESTRY=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:RetainedAncestry') -DefaultValue '512')",
    "POLYMARKET_POLYGON_RETENTION_DAYS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:RetentionDays') -DefaultValue '14')",
    "POLYMARKET_POLYGON_ENRICHMENT_WAIT_SECONDS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:EnrichmentWaitSeconds') -DefaultValue '60')",
    "POLYMARKET_POLYGON_METADATA_CACHE_CAPACITY=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:MetadataCacheCapacity') -DefaultValue '2048')",
    "POLYMARKET_POLYGON_MARKETS_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('PolymarketPolygon:MarketsUrl') -DefaultValue 'https://gamma-api.polymarket.com/markets')",
    "POLYMARKET_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:Enabled') -DefaultValue 'false')",
    "POLYMARKET_PROFILE_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:ProfileUrl') -DefaultValue 'https://gamma-api.polymarket.com/public-profile')",
    "POLYMARKET_ACTIVITY_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:ActivityUrl') -DefaultValue 'https://data-api.polymarket.com/v2/activity')",
    "POLYMARKET_TIMEOUT_SECONDS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:TimeoutSeconds') -DefaultValue '5')",
    "POLYMARKET_POLL_SECONDS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:PollSeconds') -DefaultValue '10')",
    "POLYMARKET_MAX_UNIQUE_WALLETS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:MaxUniqueWallets') -DefaultValue '20')",
    "POLYMARKET_REQUESTS_PER_MINUTE=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:RequestsPerMinute') -DefaultValue '120')",
    "POLYMARKET_HTTP_CONCURRENCY=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:HttpConcurrency') -DefaultValue '2')",
    "POLYMARKET_PAGE_SIZE=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:PageSize') -DefaultValue '100')",
    "POLYMARKET_MAXIMUM_PAGES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:MaximumPages') -DefaultValue '10')",
    "POLYMARKET_MAXIMUM_RESPONSE_BYTES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:MaximumResponseBytes') -DefaultValue '2097152')",
    "POLYMARKET_OVERLAP_MINUTES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:OverlapMinutes') -DefaultValue '5')",
    "POLYMARKET_RECONCILIATION_MINUTES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:ReconciliationMinutes') -DefaultValue '60')",
    "POLYMARKET_RECOVERY_HOURS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:RecoveryHours') -DefaultValue '24')",
    "POLYMARKET_RETENTION_DAYS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:RetentionDays') -DefaultValue '7')",
    "POLYMARKET_PROFILE_TTL_HOURS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:ProfileTtlHours') -DefaultValue '24')",
    "POLYMARKET_MISSING_PROFILE_TTL_MINUTES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:MissingProfileTtlMinutes') -DefaultValue '60')",
    "POLYMARKET_PROFILE_CACHE_CAPACITY=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Polymarket:ProfileCacheCapacity') -DefaultValue '512')",
    "HYPERLIQUID_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:Enabled') -DefaultValue 'false')",
    "HYPERLIQUID_WEB_SOCKET_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:WebSocketUrl') -DefaultValue 'wss://api.hyperliquid.xyz/ws')",
    "HYPERLIQUID_INFO_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:InfoUrl') -DefaultValue 'https://api.hyperliquid.xyz/info')",
    "HYPERLIQUID_MAX_UNIQUE_WALLETS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:MaxUniqueWallets') -DefaultValue '10')",
    "HYPERLIQUID_PING_SECONDS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:PingSeconds') -DefaultValue '20')",
    "HYPERLIQUID_PONG_TIMEOUT_SECONDS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:PongTimeoutSeconds') -DefaultValue '40')",
    "HYPERLIQUID_MAXIMUM_FRAME_BYTES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:MaximumFrameBytes') -DefaultValue '2097152')",
    "HYPERLIQUID_QUEUE_CAPACITY=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:QueueCapacity') -DefaultValue '128')",
    "HYPERLIQUID_MAXIMUM_RECOVERY_PAGES=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:MaximumRecoveryPages') -DefaultValue '8')",
    "HYPERLIQUID_RECOVERY_HOURS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:RecoveryHours') -DefaultValue '24')",
    "HYPERLIQUID_RETENTION_DAYS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:RetentionDays') -DefaultValue '7')",
    "HYPERLIQUID_AGGREGATION_MILLISECONDS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('Hyperliquid:AggregationMilliseconds') -DefaultValue '1000')",
    "TELEGRAM_BOT_TOKEN=$(Get-SecretValue -Secrets $secrets -Key 'TelegramBot:BotToken')",
    "BLOCKCHAIN_RPC_API_KEY=$(Get-FirstSecretValue -Secrets $secrets -Keys @('BlockchainNetworks:RpcApiKey', 'BlockchainNetworks:Networks:0:RpcApiKey', 'BlockchainIndexer:RpcApiKey'))",
    "BLOCKCHAIN_ENABLE_RPC_DIAGNOSTICS=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:EnableRpcDiagnostics', 'BlockchainIndexer:EnableRpcDiagnostics') -DefaultValue 'false')",
    "",
    "BLOCKCHAIN_0_CHAIN=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:0:Chain', 'BlockchainIndexer:Chain') -DefaultValue 'Bsc')",
    "BLOCKCHAIN_0_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:0:Enabled') -DefaultValue 'true')",
    "BLOCKCHAIN_0_HTTP_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:0:HttpRpcUrl', 'BlockchainIndexer:HttpRpcUrl') -DefaultValue 'https://bsc-mainnet.infura.io/v3/')",
    "BLOCKCHAIN_0_WEB_SOCKET_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:0:WebSocketRpcUrl', 'BlockchainIndexer:WebSocketRpcUrl') -DefaultValue 'wss://bsc-mainnet.infura.io/ws/v3/')",
    "",
    "BLOCKCHAIN_1_CHAIN=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:1:Chain') -DefaultValue 'Base')",
    "BLOCKCHAIN_1_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:1:Enabled') -DefaultValue 'false')",
    "BLOCKCHAIN_1_HTTP_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:1:HttpRpcUrl') -DefaultValue 'https://base-mainnet.infura.io/v3/')",
    "BLOCKCHAIN_1_WEB_SOCKET_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:1:WebSocketRpcUrl') -DefaultValue 'wss://base-mainnet.infura.io/ws/v3/')",
    "",
    "BLOCKCHAIN_2_CHAIN=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:2:Chain') -DefaultValue 'Arbitrum')",
    "BLOCKCHAIN_2_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:2:Enabled') -DefaultValue 'false')",
    "BLOCKCHAIN_2_HTTP_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:2:HttpRpcUrl') -DefaultValue 'https://arbitrum-mainnet.infura.io/v3/')",
    "BLOCKCHAIN_2_WEB_SOCKET_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:2:WebSocketRpcUrl') -DefaultValue 'wss://arbitrum-mainnet.infura.io/ws/v3/')",
    "",
    "BLOCKCHAIN_3_CHAIN=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:3:Chain') -DefaultValue 'Ethereum')",
    "BLOCKCHAIN_3_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:3:Enabled') -DefaultValue 'false')",
    "BLOCKCHAIN_3_HTTP_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:3:HttpRpcUrl') -DefaultValue 'https://mainnet.infura.io/v3/')",
    "BLOCKCHAIN_3_WEB_SOCKET_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:3:WebSocketRpcUrl') -DefaultValue 'wss://mainnet.infura.io/ws/v3/')",
    "",
    "BLOCKCHAIN_4_CHAIN=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:4:Chain') -DefaultValue 'Robinhood')",
    "BLOCKCHAIN_4_ENABLED=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:4:Enabled') -DefaultValue 'false')",
    "BLOCKCHAIN_4_HTTP_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:4:HttpRpcUrl') -DefaultValue 'https://robinhood-mainnet.infura.io/v3/')",
    "BLOCKCHAIN_4_WEB_SOCKET_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:4:WebSocketRpcUrl') -DefaultValue 'wss://robinhood-mainnet.infura.io/ws/v3/')"
)

$outputFullPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
$envLines | Set-Content -LiteralPath $outputFullPath -Encoding utf8

Write-Host "Wrote $outputFullPath from user secrets id $userSecretsId"
