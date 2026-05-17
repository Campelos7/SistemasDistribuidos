# Executado apenas na janela elevada (admin)
$ErrorActionPreference = "Continue"

$ErlangInstaller = "$env:TEMP\otp_win64_27.3.2.exe"
$ErlangUrl       = "https://github.com/erlang/otp/releases/download/OTP-27.3.2/otp_win64_27.3.2.exe"
$ErlangHome      = "C:\Program Files\Erlang OTP"
$RabbitSbin      = "C:\Program Files\RabbitMQ Server\rabbitmq_server-4.3.0\sbin"
$RabbitEtc       = "$env:APPDATA\RabbitMQ"
$LogFile         = "$env:TEMP\rabbitmq-setup-log.txt"

function Log($msg) {
    $line = "$(Get-Date -Format 'HH:mm:ss') $msg"
    Add-Content -Path $LogFile -Value $line
    Write-Host $line
}

Log "=== Inicio instalacao RabbitMQ ==="

# Parar servicos e processos
Stop-Service RabbitMQ -Force -ErrorAction SilentlyContinue
taskkill /F /IM epmd.exe 2>$null
taskkill /F /IM erl.exe 2>$null
Start-Sleep -Seconds 2

# Limpar Erlang corrompido
if (Test-Path $ErlangHome) {
    Log "A remover Erlang antigo..."
    Remove-Item -LiteralPath $ErlangHome -Recurse -Force -ErrorAction SilentlyContinue
}

# Instalar Erlang 27
if (-not (Test-Path $ErlangInstaller)) {
    Log "A descarregar Erlang 27.3.2..."
    Invoke-WebRequest -Uri $ErlangUrl -OutFile $ErlangInstaller -UseBasicParsing
}
Log "A instalar Erlang..."
Start-Process -FilePath $ErlangInstaller -ArgumentList "/S" -Wait

if (-not (Test-Path "$ErlangHome\bin\erl.exe")) {
    Log "ERRO: Erlang nao instalado!"
    Read-Host "Enter para fechar"
    exit 1
}
$otp = & "$ErlangHome\bin\erl.exe" -eval "erlang:display(erlang:system_info(otp_release)), halt()." -noshell
Log "Erlang OTP $otp OK"

# Variaveis ambiente
[Environment]::SetEnvironmentVariable("ERLANG_HOME", $ErlangHome, "Machine")
$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
foreach ($p in @("$ErlangHome\bin", $RabbitSbin)) {
    if ($machinePath -notlike "*$p*") { $machinePath = "$p;$machinePath" }
}
[Environment]::SetEnvironmentVariable("Path", $machinePath, "Machine")
$env:ERLANG_HOME = $ErlangHome
$env:Path = "$ErlangHome\bin;$RabbitSbin;" + $env:Path

if (-not (Test-Path $RabbitEtc)) { New-Item -ItemType Directory -Path $RabbitEtc -Force | Out-Null }
@"
ERLANG_HOME=$ErlangHome
NODE=rabbit@localhost
"@ | Set-Content -Path "$RabbitEtc\rabbitmq-env.conf" -Encoding ASCII
Remove-Item "$RabbitEtc\erl_crash.dump" -Force -ErrorAction SilentlyContinue

# Reinstalar servico RabbitMQ
Log "A reinstalar servico RabbitMQ..."
& "$RabbitSbin\rabbitmq-service.bat" stop 2>$null
& "$RabbitSbin\rabbitmq-service.bat" remove 2>$null
Start-Sleep -Seconds 3
& "$RabbitSbin\rabbitmq-service.bat" install
& "$RabbitSbin\rabbitmq-service.bat" start
Start-Sleep -Seconds 12

& "$RabbitSbin\rabbitmq-plugins.bat" enable rabbitmq_management 2>$null
& "$RabbitSbin\rabbitmq-service.bat" stop 2>$null
Start-Sleep -Seconds 3
& "$RabbitSbin\rabbitmq-service.bat" start
Start-Sleep -Seconds 10

$svc = Get-Service RabbitMQ
$tcp = Test-NetConnection localhost -Port 5672 -WarningAction SilentlyContinue
Log "Servico: $($svc.Status)"
Log "Porta 5672: $($tcp.TcpTestSucceeded)"

if ($svc.Status -eq "Running" -and $tcp.TcpTestSucceeded) {
    Log "=== SUCESSO - RabbitMQ pronto ==="
    Log "Web: http://localhost:15672 (guest/guest)"
} else {
    Log "=== FALHOU - ver $LogFile ==="
    & "$RabbitSbin\rabbitmq-diagnostics.bat" status 2>&1 | ForEach-Object { Log $_ }
}

Write-Host ""
Write-Host "Log guardado em: $LogFile" -ForegroundColor Cyan
Read-Host "Pressiona Enter para fechar esta janela"
