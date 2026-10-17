
$ErrorActionPreference = "Stop"

# --------------------------------------------------
# 1. Ensure Gateway__Key is configured
# --------------------------------------------------

if ([string]::IsNullOrWhiteSpace($env:Gateway__Key)) {

    # Reuse a saved user-level key if one exists
    $key = [Environment]::GetEnvironmentVariable(
        "Gateway__Key",
        [EnvironmentVariableTarget]::User
    )

    if ([string]::IsNullOrWhiteSpace($key)) {
        # Generate a cryptographically secure 32-byte key
        $bytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()

        try {
            $rng.GetBytes($bytes)
            $key = -join ($bytes | ForEach-Object {
                $_.ToString("x2")
            })
        }
        finally {
            $rng.Dispose()
        }

        [Environment]::SetEnvironmentVariable(
            "Gateway__Key",
            $key,
            [EnvironmentVariableTarget]::User
        )

        Write-Host "Generated and saved Gateway__Key." -ForegroundColor Green
    }
    else {
        Write-Host "Loaded existing Gateway__Key from user environment." -ForegroundColor Green
    }

    # Set the key in this PowerShell session
    $env:Gateway__Key = $key
}

# --------------------------------------------------
# 2. Define services and ports
# --------------------------------------------------

$root = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($root)) {
    $root = Get-Location
}

$services = @(
    @{ Name = "DefinitionService";   Port = 5001 },
    @{ Name = "ConnectorService";    Port = 5003 },
    @{ Name = "NotificationService"; Port = 5004 },
    @{ Name = "DocumentService";     Port = 5005 },
    @{ Name = "EngineService";       Port = 5002 },
    @{ Name = "ApiGateway";          Port = 5000 }
)

# --------------------------------------------------
# 3. Start .NET services
# --------------------------------------------------

foreach ($s in $services) {

    $project = Join-Path $root "src\$($s.Name)"

    if (-not (Test-Path $project)) {
        Write-Warning "Project directory not found: $project"
        continue
    }

    $title = $s.Name
    $url = "http://localhost:$($s.Port)"

    $command = @"
`$Host.UI.RawUI.WindowTitle = '$title'
Set-Location '$root'
`$env:Gateway__Key = '$env:Gateway__Key'
dotnet run --project 'src\$($s.Name)' --urls '$url'
"@

    Write-Host "Starting $title on $url ..." -ForegroundColor Cyan

    Start-Process powershell.exe -ArgumentList @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-Command", $command
    )

    Start-Sleep -Seconds 6
}

# --------------------------------------------------
# 4. Start React/Vite frontend
# --------------------------------------------------

$webPath = Join-Path $root "src\web"

if (Test-Path (Join-Path $webPath "package.json")) {

    $webCommand = @"
`$Host.UI.RawUI.WindowTitle = 'WebFrontend'
Set-Location '$webPath'
npm run dev
"@

    Write-Host "Starting WebFrontend..." -ForegroundColor Cyan

    Start-Process powershell.exe -ArgumentList @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-Command", $webCommand
    )
}
else {
    Write-Warning "Frontend package.json not found at $webPath"
}

Write-Host ""
Write-Host "Startup commands submitted." -ForegroundColor Green
Write-Host "API Gateway: http://localhost:5000"
Write-Host "Frontend: Check the Vite URL in the frontend window."
