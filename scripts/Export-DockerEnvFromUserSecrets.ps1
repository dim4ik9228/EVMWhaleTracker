param(
    [string]$ProjectPath = "src/Presentation.Telegram/Presentation.Telegram.csproj",
    [string]$OutputPath = ".env",
    [string]$SecretsPath,
    [string]$PreservedSecretsPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ConfigurationSecrets.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SecretsPath) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot $ProjectPath)
    $id = @($project.Project.PropertyGroup.UserSecretsId | Where-Object { $_ })[0]
    if (-not $id) { throw 'Project has no UserSecretsId.' }
    $SecretsPath = Join-Path $env:APPDATA "Microsoft/UserSecrets/$id/secrets.json"
}
$source = Get-FlatConfiguration (Get-Content -LiteralPath $SecretsPath -Raw | ConvertFrom-Json -AsHashtable)
$values = @{}
if ($PreservedSecretsPath) {
    $values = Get-FlatConfiguration (Get-Content -LiteralPath $PreservedSecretsPath -Raw | ConvertFrom-Json -AsHashtable)
}
foreach ($key in $source.Keys) {
    if ((Test-RetiredCredentialKey $key) -or [string]::IsNullOrWhiteSpace($source[$key])) { continue }
    if ((Test-CredentialKey $key) -or (Test-ConfidentialEndpoint $key $source[$key])) {
        # Preserve the running DB connection and endpoint credentials. New API/IPN/token keys take precedence.
        if ($values.ContainsKey($key) -and $key -eq 'ConnectionStrings:Default') {
            if ($source[$key] -and (Get-DatabasePassword $source[$key]) -cne (Get-DatabasePassword $values[$key])) { throw 'User Secrets database password differs from the preserved deployment password; reconcile it before export.' }
            continue
        }
        if ($values.ContainsKey($key) -and $key -match '(Url|Uri)$') { continue }
        if (-not [string]::IsNullOrWhiteSpace($source[$key])) { $values[$key] = $source[$key] }
    }
}
$connection = $values['ConnectionStrings:Default']
if (-not $connection) { throw 'Existing Docker database connection must be supplied or preserved before export.' }
$values['POSTGRES_PASSWORD'] = Get-DatabasePassword $connection
$lines = foreach ($key in ($values.Keys | Sort-Object)) {
    if ((Test-RetiredCredentialKey $key) -or [string]::IsNullOrWhiteSpace($values[$key])) { continue }
    if ($key -eq 'POSTGRES_PASSWORD' -or (Test-CredentialKey $key) -or (Test-ConfidentialEndpoint $key $values[$key])) {
        ConvertTo-DotEnvLine $key.Replace(':', '__') ([string]$values[$key])
    }
}
$output = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
# Validate every value before replacing an existing dotenv.
[IO.File]::WriteAllLines($output, [string[]]$lines, [Text.UTF8Encoding]::new($false))
Write-Host 'Wrote secrets-only Docker environment file.'
