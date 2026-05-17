# Arranca RabbitMQ sem servico Windows (evita problema do hostname com acentos)
# Uso: .\start-rabbitmq.ps1

$ErlangHome = "C:\Program Files\Erlang OTP"
$RabbitSbin = "C:\Program Files\RabbitMQ Server\rabbitmq_server-4.3.0\sbin"
$RabbitEtc  = "$env:APPDATA\RabbitMQ"
$LogFile    = Join-Path $PSScriptRoot "rabbitmq-server.log"
$PidFile    = Join-Path $PSScriptRoot "rabbitmq.pid"

# Configuracao (copia para AppData se ainda nao existir)
if (-not (Test-Path $RabbitEtc)) { New-Item -ItemType Directory -Path $RabbitEtc -Force | Out-Null }
Copy-Item (Join-Path $PSScriptRoot "rabbitmq-env.conf") "$RabbitEtc\rabbitmq-env.conf" -Force

$env:ERLANG_HOME = $ErlangHome
$env:RABBITMQ_NODENAME = "rabbit@localhost"
$env:Path = "$ErlangHome\bin;$RabbitSbin;" + $env:Path

# Ja esta a correr?
$tcp = Test-NetConnection localhost -Port 5672 -WarningAction SilentlyContinue
if ($tcp.TcpTestSucceeded) {
    Write-Host "[RabbitMQ] Ja esta ativo em localhost:5672" -ForegroundColor Green
    Write-Host "Interface: http://localhost:15672 (guest/guest)"
    exit 0
}

Write-Host "[RabbitMQ] A arrancar..." -ForegroundColor Yellow
$proc = Start-Process -FilePath "cmd.exe" -ArgumentList @(
    "/c",
    "set ERLANG_HOME=$ErlangHome&& set RABBITMQ_NODENAME=rabbit@localhost&& `"$RabbitSbin\rabbitmq-server.bat`" > `"$LogFile`" 2>&1"
) -PassThru -WindowStyle Hidden

$proc.Id | Set-Content $PidFile
Start-Sleep -Seconds 15

$tcp = Test-NetConnection localhost -Port 5672 -WarningAction SilentlyContinue
if ($tcp.TcpTestSucceeded) {
    Write-Host "[RabbitMQ] Ativo! Porta 5672 OK" -ForegroundColor Green
    Write-Host "Interface: http://localhost:15672 (guest/guest)"
    Write-Host "Log: $LogFile"
} else {
    Write-Host "[RabbitMQ] Falhou. Ver log: $LogFile" -ForegroundColor Red
    Get-Content $LogFile -Tail 20 -ErrorAction SilentlyContinue
    exit 1
}
