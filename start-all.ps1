if (-not $env:Gateway__Key) {
  Write-Host 'Gateway__Key is not set. Run: setx Gateway__Key "your-long-key", then reopen this terminal.'
  exit 1
}

$services = @(
  @{ Name = "DefinitionService";   Port = 5001 },
  @{ Name = "ConnectorService";    Port = 5003 },
  @{ Name = "NotificationService"; Port = 5004 },
  @{ Name = "DocumentService";     Port = 5005 },
  @{ Name = "EngineService";       Port = 5002 },
  @{ Name = "ApiGateway";          Port = 5000 }
)

foreach ($s in $services) {
  $cmd = "dotnet run --project src\$($s.Name) --urls http://localhost:$($s.Port)"
  Start-Process powershell -ArgumentList "-NoExit", "-Command", "`$Host.UI.RawUI.WindowTitle='$($s.Name)'; $cmd"
  Start-Sleep -Seconds 6
}

Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd src\web; npm run dev"