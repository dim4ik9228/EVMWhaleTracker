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
    "BLOCKCHAIN_HTTP_RPC_URL=$(Get-SecretValue -Secrets $secrets -Key 'BlockchainIndexer:HttpRpcUrl')",
    "BLOCKCHAIN_WEB_SOCKET_RPC_URL=$(Get-SecretValue -Secrets $secrets -Key 'BlockchainIndexer:WebSocketRpcUrl')"
)

$outputFullPath = Join-Path $repoRoot $OutputPath
$envLines | Set-Content -LiteralPath $outputFullPath -Encoding utf8

Write-Host "Wrote $outputFullPath from user secrets id $userSecretsId"
