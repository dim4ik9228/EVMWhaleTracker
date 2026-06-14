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
    "BLOCKCHAIN_2_WEB_SOCKET_RPC_URL=$(Get-FirstSecretValueOrDefault -Secrets $secrets -Keys @('BlockchainNetworks:Networks:2:WebSocketRpcUrl') -DefaultValue 'wss://arbitrum-mainnet.infura.io/ws/v3/')"
)

$outputFullPath = Join-Path $repoRoot $OutputPath
$envLines | Set-Content -LiteralPath $outputFullPath -Encoding utf8

Write-Host "Wrote $outputFullPath from user secrets id $userSecretsId"
