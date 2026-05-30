# Documentação do TP2 — Monitorização Urbana One Health

## 1. O que é este projeto?

Este projeto implementa o **Trabalho Prático 2** da unidade de Sistemas Distribuídos (UTAD 2025/2026). Evolui o sistema do TP1 (sensores → gateway → servidor via TCP) para uma **arquitetura distribuída** com vários processos independentes, comunicação heterogénea e código orientado a objetos.

O cenário simula **monitorização ambiental urbana** (temperatura, humidade, PM2.5, ruído, etc.) no paradigma **One Health**: dados de sensores dispersos são recolhidos, validados, normalizados, persistidos e analisados para apoiar decisões sobre saúde pública e ambiente.

| Mecanismo | Onde é usado | Tecnologia |
|-----------|--------------|------------|
| **Pub/Sub** | Sensor → Gateway | RabbitMQ (exchange `topic`) |
| **RPC** | Gateway → Pré-processamento | gRPC (porta **7001**, HTTP/2) |
| **RPC** | Servidor → Análise | gRPC (porta **7002**, HTTP/2) |
| **TCP** | Gateway → Servidor | Sockets (porta **6000**, protocolo TP1) |
| **TCP** | Interface → Servidor | Sockets (porta **6000**, `CONSULTA\|…`, `ANALISES\|…`, `ANALISE\|…`) |
| **BD** | Servidor | SQLite (`medicoes.db` na raiz do repo) |
| **CLI** | Operador | `InterfaceVisualizacao` e consola do `Sensor` |

**Porquê misturar estes mecanismos?** Cada um resolve um problema diferente: o broker desacopla produtores e consumidores; o gRPC oferece contratos tipados para lógica remota; o TCP mantém compatibilidade com o servidor do TP1; o SQLite evita infraestrutura extra; a CLI separa o operador dos processos de rede.

---

## 2. Arquitetura geral

```
┌─────────────┐     Pub/Sub      ┌──────────────┐    gRPC     ┌──────────────────┐
│   Sensor    │ ───────────────► │   Gateway    │ ──────────► │ Pré-Processamento│
│ (publica)   │    RabbitMQ      │ (subscreve)  │   :7001     │                  │
└─────────────┘                  └──────┬───────┘             └──────────────────┘
                                        │ TCP DATA|...
                                        ▼
                                 ┌──────────────┐    gRPC     ┌──────────────────┐
                                 │   Servidor   │ ──────────► │ Serviço Análise  │
                                 │ (porta 6000) │   :7002     │                  │
                                 └──────┬───────┘             └──────────────────┘
                                        │ SQLite medicoes.db
                                        │
                                 ┌──────┴─────────────┐
                                 │ Interface CLI      │  TCP CONSULTA|...
                                 │ (cliente de rede;  │  TCP ANALISE|...
                                 │  sem acesso à BD)  │──────────────────►  Servidor :6000
                                 └────────────────────┘
```

### Fluxo de uma medição (passo a passo)

1. O **sensor** publica JSON no exchange `monitorizacao.urbana` com routing key `medicao.{ZONA}.{TIPO}`.
2. O **gateway** da zona subscreve `medicao.{ZONA}.#` (wildcard `#` cobre tipos com ponto, ex. `PM2.5`), valida o sensor no `sensores.csv` e constrói um objeto `Medicao`.
3. O gateway chama **gRPC** `ProcessarMedicao` no pré-processamento (conversão de escalas, parsing JSON/XML/CSV).
4. O gateway envia a medição normalizada ao **servidor** via TCP: `DATA|sensor|zona|tipo|valor|timestamp`.
5. O servidor responde `ACK` e grava na tabela `medicoes` do SQLite.
6. O operador na **interface** pede medições e análises ao **servidor** via TCP (`CONSULTA|...`, `ANALISE|...`, `ANALISES|...`); o servidor lê/grava na BD e invoca o **ServicoAnalise** via gRPC quando necessário; a interface recebe JSON pela rede, sem abrir o SQLite.

Esta pipeline é a melhor forma de cumprir o enunciado do TP2: cada etapa tem responsabilidade única, pode ser testada e arrancada em separado, e espelha sistemas reais (edge → broker → processamento → armazenamento → analytics).

---

## 3. Estrutura da solução

```
SistemasDistribuidos/
├── Common/                    # Modelos, interfaces, protos, BD, ServidorService, messaging
├── Sensor/                    # Publica no RabbitMQ (C#)
├── SensorPython/              # Sensor alternativo em Python (valorização)
├── Gateway/                   # Subscreve, RPC pré-proc, encaminha TCP
├── PreProcessamento/          # Serviço gRPC (ASP.NET Core, porta 7001)
├── TrabalhoPratico/           # Servidor TCP (projeto Servidor.csproj)
├── ServicoAnalise/            # Serviço gRPC (porta 7002)
├── InterfaceVisualizacao/     # CLI consultas + análises
├── scripts/                   # setup/start/stop RabbitMQ (Windows)
├── medicoes.db                # Criado na raiz ao correr o servidor (auto-resolvido)
└── DOCUMENTACAO_TP2.md
```

### Porquê esta estrutura?

| Decisão | Motivo |
|---------|--------|
| **Common** | Evita duplicação; `Medicao`, protos `.proto`, `ServidorService`, repositórios e clientes gRPC são partilhados — **DIP** (Dependency Inversion). |
| **Um projeto = um processo** | Simula nós distintos num sistema distribuído; falhas e reinícios são isolados. |
| **Protos em Common** | Cliente e servidor gRPC compilam o mesmo contrato — menos erros de integração. |
| **`ServidorService` em Common** | Lógica de persistência, consultas TCP e análises RPC; instanciado no processo Servidor. |
| **`AppSettings.ResolverDbPath()`** | Procura `TrabalhoPratico.sln` e usa `medicoes.db` na raiz — só o Servidor acede ao ficheiro. |

---

## 4. Componentes em detalhe

### 4.1 Sensor (`Sensor/`)

**O que faz:** Simula um sensor urbano. Publica registo, heartbeats periódicos e medições no RabbitMQ.

**Como faz:**
1. Lê argumentos: `Sensor.exe <SensorId> <Zona> <Tipos>` — ex.: `S102 ZONA_ESCOLAR PM2.5,TEMP,RUIDO`.
2. Liga ao RabbitMQ, publica **registo** com tipos suportados.
3. Thread em background envia **heartbeat** a cada **30 s**.
4. Loop CLI até `bye`.

**Comandos CLI:**

| Comando | Descrição |
|---------|-----------|
| `data <tipo> <valor>` | Medição simples (`formato: NONE`) |
| `datajson <json>` | Payload JSON |
| `dataxml <tipo> <valor>` | Payload XML gerado pelo sensor |
| `datacsv <tipo> <valor>` | Payload CSV gerado pelo sensor |
| `bye` | Encerra o sensor |

**Routing keys (exchange topic `monitorizacao.urbana`):**

| Tipo | Padrão | Exemplo |
|------|--------|---------|
| Medição | `medicao.{ZONA}.{TIPO}` | `medicao.ZONA_ESCOLAR.PM2.5` |
| Medição JSON | `medicao.{ZONA}.JSON` | `medicao.ZONA_ESCOLAR.JSON` |
| Medição XML | `medicao.{ZONA}.XML` | `medicao.ZONA_ESCOLAR.XML` |
| Medição CSV | `medicao.{ZONA}.CSV` | `medicao.ZONA_ESCOLAR.CSV` |
| Heartbeat | `heartbeat.{ZONA}.{SENSOR_ID}` | `heartbeat.ZONA_ESCOLAR.S102` |
| Registo | `registo.{ZONA}.{SENSOR_ID}` | `registo.ZONA_ESCOLAR.S102` |

**Porquê Pub/Sub em vez de TCP direto ao gateway?**
- O sensor não precisa do IP/porta do gateway — só do broker.
- Vários gateways podem subscrever zonas diferentes no mesmo exchange.
- Mensagens persistentes (`DeliveryMode = 2`) sobrevivem a reinícios temporários do consumidor.
- Requisito explícito do TP2 e padrão habitual em IoT/edge.

**Ficheiros principais:** `SensorApp.cs`, `Publisher/RabbitMqPublisher.cs`, `SensorConfig.cs`, `Program.cs`.

---

### 4.1b Sensor Python (`SensorPython/`)

**O que faz:** Segundo produtor de medições, em **Python**, publicando no **mesmo** exchange e contrato JSON que o sensor C#. Serve para demonstrar interoperabilidade do broker (valorização do enunciado).

**Como faz:**
1. Requer Python 3 e `pip install -r requirements.txt` (biblioteca `pika`).
2. Arranque: `python sensor.py [SensorId] [Zona] [Tipos]` — predefinição `S201 ZONA_ESCOLAR PM2.5,TEMP,HUM`.
3. Comandos: `data <tipo> <valor>` e `bye` (registo e heartbeat iguais ao sensor C#).
4. O sensor **tem de constar** em `Gateway/sensores.csv` com estado `ativo` na mesma zona do gateway.

**Nota:** Não faz parte da solução Visual Studio; corre numa consola à parte, em paralelo com o sensor C# do perfil multi-startup.

**Ficheiros:** `sensor.py`, `requirements.txt`.

---

### 4.2 Gateway (`Gateway/`)

**O que faz:** Intermediário entre sensores (broker) e servidor (TCP). Valida sensores, invoca pré-processamento RPC e monitoriza heartbeats.

**Como faz:**
1. Argumento: `Gateway.exe [ZONA]` — predefinição `ZONA_ESCOLAR`.
2. Cria fila exclusiva e liga aos padrões `medicao.{zona}.#`, `heartbeat.{zona}.*`, `registo.{zona}.*`.
3. Por mensagem: valida CSV → (medição) RPC → TCP com reconexão automática.
4. `HeartbeatMonitor` a cada 30 s: sensores **ativos** sem comunicação há **>90 s** passam a **desativado** no CSV.

**Ficheiro CSV** (`Gateway/sensores.csv`):

```
S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-05-17T10:00:00
S102:ativo:ZONA_ESCOLAR:[PM2.5,TEMP,RUIDO]:2026-05-17T10:00:00
S201:ativo:ZONA_ESCOLAR:[PM2.5,TEMP,HUM]:2026-05-30T19:00:00
S103:manutencao:ZONA_INDUSTRIAL:[AR,PM10]:2026-05-16T18:30:00
```

Formato: `sensor_id:estado:zona:[tipos]:ultima_sincronizacao`

> Ao correr pelo Visual Studio, o gateway lê `Gateway/bin/Debug/net8.0/sensores.csv` (cópia do build). Novos sensores têm de existir nesse ficheiro ou ser copiados com rebuild do projeto Gateway.

**Porquê manter o CSV?**
- Continuidade com o TP1 (registo autorizado, estados `ativo` / `manutencao` / `desativado`).
- O gateway é a autoridade local: rejeita sensores desconhecidos ou inativos antes de gastar RPC/TCP.
- Timeout de heartbeat persiste estado — útil para demonstrar falhas de sensor.

**Porquê pré-processar antes do TCP?**
- Normalização (°F→°C, humidade 0–1→%) fica num serviço dedicado, escalável e testável.
- O servidor TCP permanece simples (só `DATA|...|ACK`) como no TP1.

**Ficheiros principais:** `GatewayService.cs`, `Subscriber/RabbitMqSubscriber.cs`, `RpcClient/PreProcessamentoGrpcClient.cs`, `ServerConnection/ServerForwarder.cs`, `Services/HeartbeatMonitor.cs`.

---

### 4.3 Pré-Processamento (`PreProcessamento/`)

**O que faz:** Serviço RPC que uniformiza medições antes da persistência.

**Operações:**
- **Escalas** (`EscalaConverter`): `TEMP` > 50 → assume Fahrenheit e converte para Celsius; `HUM` entre 0 e 1 → multiplica por 100.
- **Formatos** (Strategy): `JsonFormatParser`, `XmlFormatParser`, `CsvFormatParser` selecionados por `FormatParserFactory` quando `formato` ≠ `NONE` e há `payload`.

**Porquê gRPC e não REST?**
- Contrato `.proto` versionável; tipos gerados em C#; HTTP/2 adequado a chamadas frequentes gateway→serviço.
- Separação clara: o gateway é cliente fino; a lógica de parsing/conversão vive num host ASP.NET Core isolado.

**Porta:** `http://localhost:7001` (só HTTP/2)

**Arranque:** `dotnet run --project PreProcessamento`

---

### 4.4 Servidor (`TrabalhoPratico/` — projeto `Servidor`)

**O que faz:** Aceita ligações TCP dos gateways e da interface, persiste medições em SQLite e invoca o **ServiçoAnalise via gRPC** quando a interface pede análises. Escuta na porta 6000.

**Protocolo TCP:**

```
Medição:   DATA|sensor_id|zona|tipo_dado|valor|timestamp  →  ACK | ERROR
Consulta:  CONSULTA|sensorId|tipoDado|zona|desde|ate       →  CONSULTA_OK|json | CONSULTA_ERROR|msg
Análises:  ANALISES|tipo|limite                            →  ANALISES_OK|json | ANALISES_ERROR|msg
Análise:   ANALISE|tipo|sensorId|tipoDado|zona|desde|ate  →  ANALISE_OK|json | ANALISE_ERROR|msg
```

Exemplos:
- `DATA|S102|ZONA_ESCOLAR|PM2.5|78|2026-05-20T14:30:00`
- `CONSULTA|S102|PM2.5|||2026-05-01|2026-05-31` (campos vazios = sem filtro)
- `ANALISE|POLUICAO|S102|PM2.5|ZONA_ESCOLAR|2026-05-19|2026-05-21`

**Base de dados** (`medicoes.db` na raiz do repositório):

| Tabela | Conteúdo |
|--------|----------|
| `medicoes` | Medições ambientais (timestamp, sensor_id, zona, tipo_dado, valor) |
| `analises` | Resultados JSON de análises pedidas pela interface |

**Porquê SQLite?**
- Zero instalação de servidor de BD; ficheiro único; adequado ao âmbito académico e ao volume do TP.
- Apenas o **Servidor** acede ao ficheiro — a Interface obtém dados via TCP (`CONSULTA` / `ANALISES`), em linha com o princípio de processos isolados.
**Porquê uma thread por cliente?**
- Mantém o modelo de concorrência do TP1 (`AcceptTcpClient` + thread por cliente); gateways e interface podem ligar em paralelo.

**Arranque:** `dotnet run --project TrabalhoPratico`

---

### 4.5 Serviço de Análise (`ServicoAnalise/`)

**O que faz:** Análises especializadas invocadas pelo **Servidor** (via `AnaliseGrpcClient` em Common) quando a interface pede uma análise via TCP.

| Tipo (enum / string) | Classe | O que calcula |
|----------------------|--------|----------------|
| `ESTATISTICAS` | `EstatisticasAnalyzer` | Contagem, média, mín, máx, desvio padrão |
| `POLUICAO` | `PoluicaoDetector` | Alertas PM2.5 > 55, PM10 > 100, ruído > 70 dB |
| `RISCO` | `RiscoPredictor` | Índice 0–100 e classificação BAIXO/MODERADO/ALTO/CRÍTICO (TEMP escala com média e pico; PM2.5/ruído mantêm fórmulas anteriores) |

**Porquê RPC separado da interface?**
- A interface é cliente de rede: pede análises ao Servidor por TCP; o Servidor filtra medições na BD e invoca o microserviço gRPC.
- Permite evoluir algoritmos sem redeploy do CLI.

**Porta:** `http://localhost:7002`

**Arranque:** `dotnet run --project ServicoAnalise`

---

### 4.6 Interface de Visualização (`InterfaceVisualizacao/`)

**O que faz:** Menu CLI para consultar medições, pedir análises e rever histórico — **tudo via TCP** ao Servidor (porta 6000). Não acede ao SQLite.

**Menu:**
1. Consultar medições (filtros opcionais; máx. 50 linhas) — `CONSULTA|...`
2. Pedir nova análise (via Servidor -> gRPC Análise) — `ANALISE|...`
3. Ver análises guardadas — `ANALISES|...`
0. Sair

**Porquê projeto separado do Servidor?**
- O servidor deve ficar sempre à escuta de gateways e da interface; misturar menu interativo bloquearia o processo.
- A interface actua como cliente puro de rede: o Servidor é o único dono da BD, conforme Sistemas Distribuídos.

**Arranque:** `dotnet run --project InterfaceVisualizacao` (requer **Servidor** TCP a correr; opção 2 requer também **ServicoAnalise**)

---

## 5. Protocolos de comunicação

### 5.1 Pub/Sub (RabbitMQ)

- **Exchange:** `monitorizacao.urbana` (tipo **topic**, durável)
- **Corpo:** JSON (`MensagemPubSub`)

```json
{
  "tipo": "medicao",
  "sensorId": "S102",
  "zona": "ZONA_ESCOLAR",
  "tipoDado": "PM2.5",
  "valor": 78,
  "timestamp": "2026-05-20T10:15:00",
  "formato": "NONE"
}
```

Registo inclui `tiposSuportados`; medições com payload usam `formato`: `JSON`, `XML` ou `CSV`.

### 5.2 RPC Pré-processamento

- **Serviço:** `PreProcessamentoService.ProcessarMedicao`
- **Entrada:** sensor_id, zona, tipo_dado, valor, timestamp, formato, payload
- **Saída:** medição normalizada (`sucesso=true`) ou `mensagem_erro`

### 5.3 RPC Análise

- **Serviço:** `AnaliseService.ExecutarAnalise`
- **Entrada:** `tipo_analise`, filtros, `repeated MedicaoDado medicoes` (o **Servidor** envia as medições lidas da BD)
- **Saída:** `resultado_json`

### 5.4 TCP Gateway → Servidor

Linha única por medição; resposta numa linha (`ACK` / `ERROR`). `ServerForwarder` reconecta se a ligação cair.

### 5.5 TCP Interface → Servidor

| Comando | Resposta |
|---------|----------|
| `CONSULTA\|sensor\|tipo\|zona\|desde\|ate` | `CONSULTA_OK\|<json Medições>` |
| `ANALISES\|tipo\|limite` | `ANALISES_OK\|<json análises>` |
| `ANALISE\|tipo\|...\|ate` | `ANALISE_OK\|<json>` ou `ANALISE_ERROR\|msg` |

Campos vazios nos filtros significam “todos”. DTOs: `MedicaoDto`, `AnaliseDto` em `Common/Models/ConsultaDtos.cs`.

---

## 6. Padrões de design

| Padrão | Onde | Motivo |
|--------|------|--------|
| **Repository** | `SqliteMedicaoRepository`, `CsvSensorRegistoRepository` | Abstrai persistência; testável e substituível |
| **Strategy** | `IFormatParser` + JSON/XML/CSV | Novos formatos sem alterar gateway/serviço |
| **Factory** | `FormatParserFactory` | Escolhe parser pelo enum `FormatoDados` |
| **Pub/Sub** | RabbitMQ topic | Desacoplamento e routing por zona/tipo |
| **DIP** | Interfaces `IPreProcessador`, `IAnalisador`, `IMedicaoRepository` | Gateway e Servidor dependem de abstrações; Interface usa só TCP |
| **DI manual / ASP.NET DI** | `Program.cs` de cada projeto | Dependências explícitas no construtor |
| **SRP** | Uma classe por papel (publisher, subscriber, forwarder, analyzer) | Alterações localizadas |

---

## 7. Variáveis de ambiente (opcionais)

| Variável | Predefinição | Descrição |
|----------|--------------|-----------|
| `RABBIT_HOST` | localhost | Host RabbitMQ |
| `RABBIT_USER` | guest | Utilizador |
| `RABBIT_PASS` | guest | Password |
| `PREPROC_GRPC_URL` | http://localhost:7001 | URL pré-processamento |
| `ANALISE_GRPC_URL` | http://localhost:7002 | URL análise |
| `SERVIDOR_HOST` | 127.0.0.1 | Host servidor TCP |
| `SERVIDOR_PORT` | 6000 | Porta servidor |
| `DB_PATH` | *(auto: raiz do repo)* | Caminho SQLite; se vazio, procura `TrabalhoPratico.sln` |

---

## 8. Como executar

### Pré-requisitos

- .NET 8 SDK
- RabbitMQ em execução (obrigatório para Sensor e Gateway)
- Python 3 + `pika` (opcional; só para `SensorPython/`)

#### RabbitMQ no Windows (sem Docker)

**Importante:** RabbitMQ 4.3 requer **Erlang 27** (não instalar Erlang 29 via winget — incompatível).

**Instalação inicial (uma vez, com UAC):**

```powershell
cd scripts
Set-ExecutionPolicy Bypass -Scope Process -Force
.\setup-rabbitmq.ps1
```

**Arrancar (antes de Gateway/Sensor):**

```powershell
cd scripts
.\start-rabbitmq.ps1
```

**Parar:**

```powershell
.\stop-rabbitmq.ps1
```

> Hostnames com acentos podem impedir o serviço Windows; os scripts usam `rabbit@localhost`.

Interface web: http://localhost:15672 (`guest` / `guest`)

**Docker (alternativa):**

```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

### Visual Studio — perfil de arranque múltiplo

1. `.\scripts\start-rabbitmq.ps1`
2. Perfil **`TP2 - Sistema Completo`** (ficheiro `TrabalhoPratico.slnLaunch.user`) — arranca 5 processos:
   - PreProcessamento, ServicoAnalise, Servidor, Gateway (`ZONA_ESCOLAR`), Sensor (`S102 ZONA_ESCOLAR PM2.5,TEMP,RUIDO`)
3. Interface à parte (botão direito → **Start New Instance** ou):

```powershell
dotnet run --project InterfaceVisualizacao
```

4. *(Opcional)* Sensor Python à parte:

```powershell
cd SensorPython
pip install -r requirements.txt
python sensor.py S201 ZONA_ESCOLAR PM2.5,TEMP,HUM
```

Perfil alternativo **`TP2 - Servidor + Gateway + Sensor`**: sem serviços gRPC (útil só se não houver medições RPC/análise).

### Terminais manuais (ordem recomendada)

```powershell
# 0 — Broker
cd scripts
.\start-rabbitmq.ps1
cd ..

# 1 — Pré-processamento
dotnet run --project PreProcessamento

# 2 — Análise
dotnet run --project ServicoAnalise

# 3 — Servidor TCP
dotnet run --project TrabalhoPratico

# 4 — Gateway
dotnet run --project Gateway -- ZONA_ESCOLAR

# 5 — Sensor C#
dotnet run --project Sensor -- S102 ZONA_ESCOLAR PM2.5,TEMP,RUIDO

# 5b — Sensor Python (opcional)
cd SensorPython
python sensor.py S201 ZONA_ESCOLAR PM2.5,TEMP,HUM
cd ..

# 6 — Interface (requer Servidor a correr)
dotnet run --project InterfaceVisualizacao
```

---

## 9. Exemplos de comandos e respostas

### 9.1 Sensor — medição simples

**Comando:**

```
> data PM2.5 78
```

**Saída esperada (sensor):**

```
[SENSOR] Medição publicada: PM2.5=78
```

**Saída esperada (gateway):**

```
[GATEWAY] Recebido (medicao.ZONA_ESCOLAR.PM2.5): medicao de S102
[GATEWAY] Ligado ao servidor 127.0.0.1:6000
[GATEWAY] Servidor respondeu: ACK
```

**Saída esperada (servidor):**

```
[SERVIDOR] Gateway conectado.
[SERVIDOR] Recebido: DATA|S102|ZONA_ESCOLAR|PM2.5|78|2026-05-20T14:30:00
[SERVIDOR] Medição guardada: S102 | PM2.5 | 78
```

---

### 9.2 Sensor — temperatura em Fahrenheit

**Comando:**

```
> data TEMP 86
```

O pré-processamento converte 86 °F → **30 °C** (regra: `TEMP` com valor > 50).

**TCP enviado ao servidor (aproximado):**

```
DATA|S102|ZONA_ESCOLAR|TEMP|30|2026-05-20T14:31:00
```

---

### 9.3 Sensor — medição JSON (humidade 0–1)

**Comando:**

```
> datajson {"sensorId":"S102","zona":"ZONA_ESCOLAR","tipoDado":"HUM","valor":0.65,"timestamp":"2026-05-20T12:00:00"}
```

Gateway publica com `formato: JSON`; pré-processamento converte **0.65 → 65** (%).

---

### 9.4 Sensor — medição XML e CSV

**Comandos:**

```
> dataxml TEMP 25
> datacsv RUIDO 60
```

Routing keys `medicao.ZONA_ESCOLAR.XML` e `.CSV`; o tipo real (`TEMP`, `RUIDO`) é extraído no pré-processamento.

---

### 9.5 Sensor — encerrar

```
> bye
```

```
[SENSOR] Encerrado.
```

---

### 9.6 Gateway — sensor não registado

Sensor `S999` não está em `sensores.csv`:

```
[GATEWAY] Recebido (medicao.ZONA_ESCOLAR.PM2.5): medicao de S999
[GATEWAY] Medição rejeitada — sensor inválido ou inativo.
```

---

### 9.7 Gateway — tipo não suportado

Sensor S102 não tem `HUM` na lista `[PM2.5,TEMP,RUIDO]` (medição `formato: NONE`):

```
[GATEWAY] Tipo HUM não suportado por S102.
```

---

### 9.8 Interface — consultar medições

```
Escolha: 1
...
--- Medições ---
2026-05-20 14:30 | S102 | ZONA_ESCOLAR | PM2.5 | 78
Total mostrado: 1
```

(Cabeçalho da interface: `Servidor (TCP): 127.0.0.1:6000` — confirma que não há acesso directo à BD.)

---

### 9.9 Interface — análise de poluição

```
Escolha: 2
Tipos: 1=Estatisticas 2=Poluicao 3=Risco
2
ID do sensor: S102
...

--- Resultado da análise ---
{"totalAlertas":1,"alertas":[{"sensorId":"S102","zona":"ZONA_ESCOLAR","tipoDado":"PM2.5","valor":78,"timestamp":"2026-05-20T14:30:00","nivel":"ELEVADO"}]}
```

---

### 9.10 Interface — estatísticas sem dados

Se não houver medições no intervalo:

```
{"erro":"Sem medições para analisar."}
```

---

## 10. Diferenças face ao TP1

| TP1 | TP2 |
|-----|-----|
| Tudo `static` num `Program.cs` | Classes instanciadas, injeção no construtor |
| Sem interfaces | `IPreProcessador`, `IAnalisador`, `IMedicaoRepository`, etc. |
| TCP sensor→gateway | RabbitMQ Pub/Sub sensor→broker→gateway |
| Sem RPC | gRPC pré-processamento + análise |
| Parsing manual frágil | Entidade `Medicao` com validação |
| Monólito | 6 executáveis + biblioteca Common |
| BD implícita | `AppSettings` resolve `medicoes.db` na raiz do repo |

---

## 11. Valorização (extras)

- Pré-processamento **multi-formato** (JSON, XML, CSV) com comandos `datajson`, `dataxml`, `datacsv` no sensor C#
- **Sensor em Python** (`SensorPython/`) no mesmo broker — interoperabilidade entre linguagens
- **Três analisadores** (estatísticas, poluição, risco One Health)
- Interface **100% via TCP** (`CONSULTA`, `ANALISES`, `ANALISE`); Servidor único dono da BD
- **Reconexão TCP** no gateway; listener do servidor tolerante a desconexões de clientes
- **Heartbeat** com desativação automática no CSV; routing `medicao.{zona}.#` para tipos como `PM2.5`
- Scripts PowerShell para RabbitMQ no Windows (detecção de instalação)
- Perfil Visual Studio **multi-startup**

---

## 12. Resolução de problemas

| Problema | Causa provável | Solução |
|----------|----------------|---------|
| `Connection refused` na porta 5672 | RabbitMQ parado | `.\scripts\start-rabbitmq.ps1` ou Docker |
| `StatusCode(Unavailable)` gRPC | Pré-proc ou Análise não arrancados | Arrancar projetos nas portas 7001/7002 **antes** dos clientes |
| Gateway não recebe mensagens | Zona diferente ou routing | Gateway: `ZONA_ESCOLAR`; sensor: mesma zona; tipos com `.` exigem subscrição `#` |
| Medição rejeitada | Sensor ausente/inativo no CSV | Editar `Gateway/sensores.csv` (e cópia em `bin/Debug/net8.0/` se correr pelo VS); estado `ativo` |
| Interface sem dados / erro ligação | Servidor parado | Arrancar Servidor (porta 6000) antes da Interface; opção 2 requer ServicoAnalise |
| Sensor Python rejeitado | S201 fora do CSV em runtime | Rebuild Gateway ou editar `bin/Debug/net8.0/sensores.csv` |
| TEMP “estranha” na BD | Conversão °F→°C | Valores > 50 em `TEMP` são tratados como Fahrenheit |
| Serviço Windows RabbitMQ falha | Hostname com acentos | Usar scripts com `rabbit@localhost` |

---

## 13. Onde começar a ler o código

1. `Common/Models/Medicao.cs` — entidade e formato TCP
2. `Gateway/Services/GatewayService.cs` — fluxo principal do gateway
3. `Common/Services/ServidorService.cs` — TCP + análises + consultas
4. `TrabalhoPratico/Networking/GatewayTcpListener.cs` — aceita gateways
5. `Common/Protos/*.proto` — contratos gRPC

Todo o código C# público inclui comentários `/// <summary>`; nomes de domínio em português (`Medicao`, `Zona`, `SensorRegisto`).

---

*Documentação alinhada com o estado atual do repositório (Interface via TCP, sensor Python, CONSULTA/ANALISES) — Sistemas Distribuídos UTAD 2025/2026.*
