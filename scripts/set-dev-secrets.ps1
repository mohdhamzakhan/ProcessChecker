
$ErrorActionPreference = "Stop"

$services = @(
    "DefinitionService",
    "ConnectorService",
    "EngineService",
    "NotificationService",
    "DocumentService",
    "ApiGateway"
)

# --------------------------------------------------
# 1. Check for conflicting environment variables
# --------------------------------------------------

foreach ($name in @("ConnectionStrings__Default", "Gateway__Key")) {
    foreach ($scope in @("Process", "User", "Machine")) {
        $value = [Environment]::GetEnvironmentVariable($name, $scope)

        if (-not [string]::IsNullOrEmpty($value)) {
            throw "$name is still set at $scope level. Remove it and restart your terminal/Visual Studio before running this script."
        }
    }
}

# --------------------------------------------------
# 2. Read and validate Oracle credentials
# --------------------------------------------------

$user = Read-Host "Enter Oracle username"
$src  = Read-Host "Enter data source (e.g. 101.135.120.233:1521/MEAITESTDBPDB)"
$sec  = Read-Host "Enter Oracle password" -AsSecureString

if ([string]::IsNullOrWhiteSpace($user)) {
    throw "Oracle username is required."
}

if ([string]::IsNullOrWhiteSpace($src)) {
    throw "Oracle data source is required."
}

if ($null -eq $sec -or $sec.Length -eq 0) {
    throw "Oracle password is required."
}

# Convert SecureString to plaintext only when needed.
$bstr = [IntPtr]::Zero
$pw = $null

try {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    $pw = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
}
finally {
    if ($bstr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($pw)) {
    throw "Oracle password is required."
}

# Prevent unsupported double quotes in the password.
if ($pw.Contains('"')) {
    throw 'A password containing a double quote is not supported by this script.'
}

# Quote password when it contains connection-string delimiters
# or whitespace.
if ($pw -match '[;=\s]') {
    $pw = '"' + $pw + '"'
}

$cs = "User Id=$user;Password=$pw;Data Source=$src"

# --------------------------------------------------
# 3. Generate shared Gateway key
# --------------------------------------------------

$random = [Security.Cryptography.RandomNumberGenerator]::Create()

try {
    $gatewayBytes = New-Object byte[] 32
    $random.GetBytes($gatewayBytes)

    $gatewayKey = -join (
        $gatewayBytes | ForEach-Object { $_.ToString("x2") }
    )
}
finally {
    $random.Dispose()
}

# --------------------------------------------------
# 4. Set user secrets for all services
# --------------------------------------------------

foreach ($service in $services) {
    $project = Join-Path "src" $service

    if (-not (Test-Path $project)) {
        throw "Project directory not found: $project"
    }

    Write-Host "Configuring $service..." -ForegroundColor Cyan

    dotnet user-secrets init --project $project | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to initialize user secrets for $service."
    }

    dotnet user-secrets set "ConnectionStrings:Default" $cs --project $project | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set Oracle connection string for $service."
    }

    dotnet user-secrets set "Gateway:Key" $gatewayKey --project $project | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set Gateway key for $service."
    }

    Write-Host "Configured $service" -ForegroundColor Green
}

# --------------------------------------------------
# 5. Create JWT key if missing
# --------------------------------------------------

$apiProject = Join-Path "src" "ApiGateway"

$existingSecrets = dotnet user-secrets list --project $apiProject

if ($LASTEXITCODE -ne 0) {
    throw "Failed to read ApiGateway user secrets."
}

if (-not ($existingSecrets -match '(?m)^Jwt:Key\s*=')) {
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $jwtBytes = New-Object byte[] 48
        $random.GetBytes($jwtBytes)

        $jwtKey = -join (
            $jwtBytes | ForEach-Object { $_.ToString("x2") }
        )
    }
    finally {
        $random.Dispose()
    }

    dotnet user-secrets set "Jwt:Key" $jwtKey --project $apiProject | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set ApiGateway JWT key."
    }

    Write-Host "Configured ApiGateway Jwt:Key" -ForegroundColor Green
}
else {
    Write-Host "ApiGateway Jwt:Key already exists; keeping existing key." -ForegroundColor Yellow
}

# --------------------------------------------------
# 6. Finish
# --------------------------------------------------

# Clear sensitive variables from this script's scope.
$pw = $null
$cs = $null
$sec = $null
$gatewayKey = $null
$jwtKey = $null

Write-Host ""
Write-Host "All development secrets configured successfully." -ForegroundColor Green
Write-Host "Services: $($services -join ', ')"
Write-Host "Restart your services to load the updated secrets."
