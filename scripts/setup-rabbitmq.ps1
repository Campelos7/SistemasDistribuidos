# =============================================================================
# Setup RabbitMQ para TP2 (Windows, sem Docker)
#   cd C:\Users\tomas\source\repos\SistemasDistribuidos\scripts
#   .\setup-rabbitmq.ps1
# (Se nao for admin, abre janela elevada automaticamente — clica Sim no UAC)
# =============================================================================

# Auto-elevar para Administrador se necessario
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $elevated = Join-Path $PSScriptRoot "install-rabbitmq-elevated.ps1"
    Write-Host "A abrir janela de ADMINISTRADOR (clica SIM no UAC)..." -ForegroundColor Yellow
    Write-Host "Aguarda na NOVA janela ate ver SUCESSO ou FALHOU." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$elevated`""
    )
    Write-Host "`nNesta janela podes verificar depois com:" -ForegroundColor Gray
    Write-Host "  Get-Service RabbitMQ" -ForegroundColor Gray
    Write-Host "  Test-NetConnection localhost -Port 5672" -ForegroundColor Gray
    exit
}

$ErrorActionPreference = "Stop"

$ErlangUrl      = "https://github.com/erlang/otp/releases/download/OTP-27.3.2/otp_win64_27.3.2.exe"
$ErlangInstaller = "$env:TEMP\otp_win64_27.3.2.exe"
$ErlangHome     = "C:\Program Files\Erlang OTP"
$RabbitSbin     = "C:\Program Files\RabbitMQ Server\rabbitmq_server-4.3.0\sbin"
$RabbitEtc      = "$env:APPDATA\RabbitMQ"

Write-Host "=== Setup RabbitMQ TP2 ===" -ForegroundColor Cyan

# --- Limpar instalações antigas / corrompidas ---
Write-Host "`n[1/6] A limpar Erlang antigo e processos epmd..." -ForegroundColor Yellow
Stop-Service RabbitMQ -Force -ErrorAction SilentlyContinue
taskkill /F /IM epmd.exe 2>$null
taskkill /F /IM erl.exe 2>$null
Start-Sleep -Seconds 2

winget uninstall Erlang.ErlangOTP --accept-source-agreements 2>$null
if (Test-Path $ErlangHome) {
    Remove-Item -LiteralPath $ErlangHome -Recurse -Force
}

# --- Instalar Erlang 27 (compatível com RabbitMQ 4.3) ---
Write-Host "`n[2/6] A instalar Erlang 27.3.2..." -ForegroundColor Yellow
if (-not (Test-Path $ErlangInstaller)) {
    Invoke-WebRequest -Uri $ErlangUrl -OutFile $ErlangInstaller -UseBasicParsing
}
Start-Process -FilePath $ErlangInstaller -ArgumentList "/S" -Wait

$erl = "$ErlangHome\bin\erl.exe"
if (-not (Test-Path $erl)) { throw "Erlang nao instalado em $ErlangHome" }
$otp = & $erl -eval "erlang:display(erlang:system_info(otp_release)), halt()." -noshell
Write-Host "Erlang OTP $otp OK" -ForegroundColor Green

# --- Variáveis de ambiente ---
Write-Host "`n[3/6] A configurar ERLANG_HOME e PATH..." -ForegroundColor Yellow
[Environment]::SetEnvironmentVariable("ERLANG_HOME", $ErlangHome, "Machine")
$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
foreach ($p in @("$ErlangHome\bin", $RabbitSbin)) {
    if ($machinePath -notlike "*$p*") { $machinePath = "$p;$machinePath" }
}
[Environment]::SetEnvironmentVariable("Path", $machinePath, "Machine")
$env:ERLANG_HOME = $ErlangHome
$env:Path = "$ErlangHome\bin;$RabbitSbin;" + $env:Path

if (-not (Test-Path $RabbitEtc)) { New-Item -ItemType Directory -Path $RabbitEtc -Force | Out-Null }
"ERLANG_HOME=$ErlangHome" | Set-Content -Path "$RabbitEtc\rabbitmq-env.conf" -Encoding ASCII

# Limpar crash dumps antigos
Remove-Item "$RabbitEtc\erl_crash.dump" -Force -ErrorAction SilentlyContinue

# --- Reinstalar serviço RabbitMQ ---
Write-Host "`n[4/6] A reinstalar servico RabbitMQ..." -ForegroundColor Yellow
& "$RabbitSbin\rabbitmq-service.bat" stop 2>$null
& "$RabbitSbin\rabbitmq-service.bat" remove 2>$null
Start-Sleep -Seconds 2
& "$RabbitSbin\rabbitmq-service.bat" install
& "$RabbitSbin\rabbitmq-service.bat" start
Start-Sleep -Seconds 10

# --- Plugin gestão web ---
Write-Host "`n[5/6] A ativar rabbitmq_management..." -ForegroundColor Yellow
& "$RabbitSbin\rabbitmq-plugins.bat" enable rabbitmq_management
& "$RabbitSbin\rabbitmq-service.bat" stop
Start-Sleep -Seconds 3
& "$RabbitSbin\rabbitmq-service.bat" start
Start-Sleep -Seconds 8

# --- Verificação ---
Write-Host "`n[6/6] Verificacao..." -ForegroundColor Yellow
$svc = Get-Service RabbitMQ
Write-Host "Servico: $($svc.Status)"
$tcp = Test-NetConnection localhost -Port 5672 -WarningAction SilentlyContinue
Write-Host "Porta 5672: $($tcp.TcpTestSucceeded)"
& "$RabbitSbin\rabbitmq-diagnostics.bat" ping 2>&1

if ($svc.Status -eq "Running" -and $tcp.TcpTestSucceeded) {
    Write-Host "`n=== RabbitMQ PRONTO ===" -ForegroundColor Green
    Write-Host "Interface: http://localhost:15672  (guest / guest)"
    Write-Host "Podes agora correr Gateway e Sensor do TP2."
} else {
    Write-Host "`n=== Algo falhou ===" -ForegroundColor Red
    Write-Host "Ver logs em: $RabbitEtc\log"
    exit 1
}
