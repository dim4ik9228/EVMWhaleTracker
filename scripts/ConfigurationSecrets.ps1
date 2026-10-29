# Shared pure helpers; callers supply files explicitly.
function Get-FlatConfiguration {
    param([System.Collections.IDictionary]$Values, [string]$Prefix = '')
    $result = @{}
    foreach ($key in $Values.Keys) {
        $name = if ($Prefix) { "$Prefix`:$key" } else { [string]$key }
        $value = $Values[$key]
        if ($value -is [System.Collections.IDictionary]) {
            $nested = Get-FlatConfiguration $value $name
            foreach ($entry in $nested.Keys) { $result[$entry] = $nested[$entry] }
        } elseif ($value -is [array]) {
            for ($i = 0; $i -lt $value.Count; $i++) {
                $nested = Get-FlatConfiguration @{ "$i" = $value[$i] } $name
                foreach ($entry in $nested.Keys) { $result[$entry] = $nested[$entry] }
            }
        } elseif ($null -ne $value) { $result[$name.Replace('__', ':')] = [string]$value }
    }
    return $result
}
function Test-CredentialKey {
    param([string]$Key)
    return $Key -match '^(TelegramBot:BotToken|ConnectionStrings:Default|Billing:NowPayments(ApiKey|IpnSecret)|BlockchainNetworks:RpcApiKey|BlockchainNetworks:Networks:[0-4]:RpcApiKey|PolymarketPolygon:RpcApiKey)$'
}
function Test-RetiredCredentialKey {
    param([string]$Key)
    return $Key -match '^Billing:NowPayments(Email|Password|Username|Jwt|AccessToken|RefreshToken|Dashboard.*)$' -or $Key -match '^Google:'
}
function Test-ConfidentialEndpoint {
    param([string]$Key, [string]$Value)
    if ($Key -notmatch '(Url|Uri)$' -or -not $Value) { return $false }
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri)) { return $true }
    if ($uri.UserInfo -or $uri.Query -or $uri.Fragment) { return $true }
    if ($Key -match '(HttpRpcUrl|WebSocketRpcUrl)$') {
        # Only the checked-in public provider bases are known to contain no credentials.
        return $Value -notmatch '^(https|wss)://(bsc-mainnet|base-mainnet|arbitrum-mainnet|mainnet|robinhood-mainnet|polygon-mainnet)\.infura\.io/(ws/)?v3/$'
    }
    return $false
}
function ConvertTo-DotEnvLine {
    param([string]$Key, [string]$Value)
    if ($Value.Contains("`r") -or $Value.Contains("`n") -or $Value.Contains([char]0)) {
        throw "Credential values must be single-line and contain no NUL."
    }
    # Escape for Compose dotenv double quotes; doubled dollars suppress interpolation.
    $escaped = $Value.Replace('\', '\\').Replace('"', '\"').Replace('$', '$$')
    return $Key + '="' + $escaped + '"'
}

function Get-DatabasePassword {
    param([string]$Connection)
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($Connection)
    if ($builder.ContainsKey('Password')) { return [string]$builder['Password'] }
    if ($builder.ContainsKey('Pwd')) { return [string]$builder['Pwd'] }
    throw 'Existing database password is required.'
}
