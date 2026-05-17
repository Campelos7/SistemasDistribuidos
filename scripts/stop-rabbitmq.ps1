# Para o RabbitMQ arrancado por start-rabbitmq.ps1
$PidFile = Join-Path $PSScriptRoot "rabbitmq.pid"

Get-Process erl, epmd -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path $PidFile) { Remove-Item $PidFile -Force }

Write-Host "[RabbitMQ] Parado." -ForegroundColor Yellow
