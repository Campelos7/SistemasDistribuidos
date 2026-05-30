# Arranca RabbitMQ sem servico Windows (evita problema do hostname com acentos)
# Uso: .\start-rabbitmq.ps1

$ErlangHome = "C:\Program Files\Erlang OTP"
$RabbitBase = "C:\Program Files\RabbitMQ Server"
$RabbitEtc  = "$env:APPDATA\RabbitMQ"
$LogFile    = Join-Path $PSScriptRoot "rabbitmq-server.log"
$PidFile    = Join-Path $PSScriptRoot "rabbitmq.pid"

function Resolve-RabbitSbin {
    $fixed = Join-Path $RabbitBase "rabbitmq_server-4.3.0\sbin"
    if (Test-Path (Join-Path $fixed "rabbitmq-server.bat")) { return $fixed }

    if (Test-Path $RabbitBase) {
        $latest = Get-ChildItem $RabbitBase -Directory -Filter "rabbitmq_server-*" |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($latest) {
            $candidate = Join-Path $latest.FullName "sbin"
            if (Test-Path (Join-Path $candidate "rabbitmq-server.bat")) { return $candidate }
        }
    }

    return $fixed
}

function Show-InstallHelp {
    Write-Host ""
    Write-Host "RabbitMQ/Erlang nao estao instalados neste PC." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Instalacao (uma vez, por ordem):" -ForegroundColor Cyan
    Write-Host "  1. Erlang 27:  https://github.com/erlang/otp/releases/download/OTP-27.3.2/otp_win64_27.3.2.exe"
    Write-Host "  2. RabbitMQ 4.3: https://github.com/rabbitmq/rabbitmq-server/releases/download/v4.3.0/rabbitmq-server-4.3.0.exe"
    Write-Host "  3. Depois, nesta pasta (como Administrador): .\setup-rabbitmq.ps1"
    Write-Host "  4. Por fim: .\start-rabbitmq.ps1"
    Write-Host ""
}

$RabbitSbin = Resolve-RabbitSbin

if (-not (Test-Path (Join-Path $ErlangHome "bin\erl.exe"))) {
    Write-Host "[RabbitMQ] Erlang nao encontrado em: $ErlangHome" -ForegroundColor Red
    Show-InstallHelp
    exit 1
}

if (-not (Test-Path (Join-Path $RabbitSbin "rabbitmq-server.bat"))) {
    Write-Host "[RabbitMQ] rabbitmq-server.bat nao encontrado em: $RabbitSbin" -ForegroundColor Red
    Show-InstallHelp
    exit 1
}

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
