# Documentação do TP2 — Monitorização Urbana One Health

## 1. O que é este projeto?

Este projeto implementa o **Trabalho Prático 2** da unidade de Sistemas Distribuídos (UTAD 2025/2026). Evolui o sistema do TP1 (sensores → gateway → servidor via TCP) para uma **arquitetura distribuída** com vários processos independentes, comunicação heterogénea e código orientado a objetos.

O cenário simula **monitorização ambiental urbana** (temperatura, humidade, PM2.5, ruído, etc.) no paradigma **One Health**: dados de sensores dispersos são recolhidos, validados, normalizados, persistidos e analisados para apoiar decisões sobre saúde pública e ambiente.

| Mecanismo | Onde é usado | Tecnologia |
|-----------|--------------|------------|
| **Pub/Sub** | Sensor → Gateway | RabbitMQ (exchange `topic`) |
| **RPC** | Gateway → Pré-processamento | gRPC (porta **7001**, HTTP/2) |
| **RPC** | Interface → Análise | gRPC (porta **7002**, HTTP/2) |
| **TCP** | Gateway → Servidor | Sockets (porta **6000**, protocolo TP1) |
| **BD** | Servidor + Interface | SQLite (`medicoes.db` na raiz do repo) |
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
                                 ┌──────────────┐
                                 │   Servidor   │  SQLite medicoes.db
                                 │ (porta 6000) │
                                 └──────┬───────┘
                                        │ lê/grava mesma BD
                                        ▼
                                 ┌──────────────────┐    gRPC     ┌──────────────────┐
                                 │ Interface CLI    │ ──────────► │ Serviço Análise  │
                                 │ (consultas +     │   :7002     │                  │
                                 │  pedidos análise)│             └──────────────────┘
                                 └──────────────────┘
```

### Fluxo de uma medição (passo a passo)

1. O **sensor** publica JSON no exchange `monitorizacao.urbana` com routing key `medicao.{ZONA}.{TIPO}`.
2. O **gateway** da zona subscreve `medicao.{ZONA}.*`, valida o sensor no `sensores.csv` e constrói um objeto `Medicao`.
3. O gateway chama **gRPC** `ProcessarMedicao` no pré-processamento (conversão de escalas, parsing JSON/XML/CSV).
4. O gateway envia a medição normalizada ao **servidor** via TCP: `DATA|sensor|zona|tipo|valor|timestamp`.
5. O servidor responde `ACK` e grava na tabela `medicoes` do SQLite.
6. Mais tarde, o operador na **interface** filtra medições na BD e pede uma **análise** via gRPC; o resultado JSON fica na tabela `analises`.

Esta pipeline é a melhor forma de cumprir o enunciado do TP2: cada etapa tem responsabilidade única, pode ser testada e arrancada em separado, e espelha sistemas reais (edge → broker → processamento → armazenamento → analytics).

---

## 3. Estrutura da solução

```
SistemasDistribuidos/
├── Common/                    # Modelos, interfaces, protos, BD, ServidorService, messaging
├── Sensor/                    # Publica no RabbitMQ
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
| **`ServidorService` em Common** | Servidor TCP e Interface usam a mesma lógica de persistência e análise sem referenciar o executável um do outro. |
| **`AppSettings.ResolverDbPath()`** | Procura `TrabalhoPratico.sln` e usa `medicoes.db` na raiz — Servidor e Interface veem os mesmos dados sem configurar caminhos à mão. |

---

## 4. Componentes em detalhe

### 4.1 Sensor (`Sensor/`)

**O que faz:** Simula um sensor urbano. Publica registo, heartbeats periódicos e medições no RabbitMQ.

**Como faz:**
1. Lê argumentos: `Sensor.exe <SensorId> <Zona> <Tipos>` — ex.: `S102 ZONA_ESCOLAR PM2.5,TEMP,RUIDO`.
2. Liga ao RabbitMQ, publica **registo** com tipos suportados.
3. Thread em background envia **heartbeat** a cada **30 s**.
4. Loop CLI até `bye`.

**Routing keys (exchange topic `monitorizacao.urbana`):**

| Tipo | Padrão | Exemplo |
|------|--------|---------|
| Medição | `medicao.{ZONA}.{TIPO}` | `medicao.ZONA_ESCOLAR.PM2.5` |
| Medição JSON | `medicao.{ZONA}.JSON` | `medicao.ZONA_ESCOLAR.JSON` |
| Heartbeat | `heartbeat.{ZONA}.{SENSOR_ID}` | `heartbeat.ZONA_ESCOLAR.S102` |
| Registo | `registo.{ZONA}.{SENSOR_ID}` | `registo.ZONA_ESCOLAR.S102` |

**Porquê Pub/Sub em vez de TCP direto ao gateway?**
- O sensor não precisa do IP/porta do gateway — só do broker.
- Vários gateways podem subscrever zonas diferentes no mesmo exchange.
- Mensagens persistentes (`DeliveryMode = 2`) sobrevivem a reinícios temporários do consumidor.
- Requisito explícito do TP2 e padrão habitual em IoT/edge.

**Ficheiros principais:** `SensorApp.cs`, `Publisher/RabbitMqPublisher.cs`, `SensorConfig.cs`, `Program.cs`.

---

### 4.2 Gateway (`Gateway/`)

**O que faz:** Intermediário entre sensores (broker) e servidor (TCP). Valida sensores, invoca pré-processamento RPC e monitoriza heartbeats.

**Como faz:**
1. Argumento: `Gateway.exe [ZONA]` — predefinição `ZONA_ESCOLAR`.
2. Cria fila exclusiva e liga aos padrões `medicao.{zona}.*`, `heartbeat.{zona}.*`, `registo.{zona}.*`.
3. Por mensagem: valida CSV → (medição) RPC → TCP com reconexão automática.
4. `HeartbeatMonitor` a cada 30 s: sensores **ativos** sem comunicação há **>90 s** passam a **desativado** no CSV.

**Ficheiro CSV** (`Gateway/sensores.csv`):

```
S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-05-17T10:00:00
S102:ativo:ZONA_ESCOLAR:[PM2.5,TEMP,RUIDO]:2026-05-17T10:00:00
S103:manutencao:ZONA_INDUSTRIAL:[AR,PM10]:2026-05-16T18:30:00
```

Formato: `sensor_id:estado:zona:[tipos]:ultima_sincronizacao`

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

**O que faz:** Aceita ligações TCP dos gateways, persiste medições em SQLite. **Não** expõe CLI — só escuta a porta 6000.

**Protocolo TCP (herdado do TP1):**

```
Pedido:  DATA|sensor_id|zona|tipo_dado|valor|timestamp
Resposta: ACK
         ou ERROR
```

Exemplo: `DATA|S102|ZONA_ESCOLAR|PM2.5|78|2026-05-20T14:30:00`

**Base de dados** (`medicoes.db` na raiz do repositório):

| Tabela | Conteúdo |
|--------|----------|
| `medicoes` | Medições ambientais (timestamp, sensor_id, zona, tipo_dado, valor) |
| `analises` | Resultados JSON de análises pedidas pela interface |

**Porquê SQLite?**
- Zero instalação de servidor de BD; ficheiro único partilhado; adequado ao âmbito académico e ao volume do TP.

**Porquê uma thread por gateway?**
- Mantém o modelo de concorrência do TP1 (`AcceptTcpClient` + thread por cliente); gateways podem enviar em paralelo.

**Arranque:** `dotnet run --project TrabalhoPratico`

---

### 4.5 Serviço de Análise (`ServicoAnalise/`)

**O que faz:** Análises especializadas invocadas pela **Interface** (via `AnaliseGrpcClient` em Common). O servidor TCP não chama este serviço diretamente.

| Tipo (enum / string) | Classe | O que calcula |
|----------------------|--------|----------------|
| `ESTATISTICAS` | `EstatisticasAnalyzer` | Contagem, média, mín, máx, desvio padrão |
| `POLUICAO` | `PoluicaoDetector` | Alertas PM2.5 > 55, PM10 > 100, ruído > 70 dB |
| `RISCO` | `RiscoPredictor` | Índice 0–100 e classificação BAIXO/MODERADO/ALTO/CRÍTICO |

**Porquê RPC separado da interface?**
- A interface é “fina”: lê BD local e delega cálculo pesado/regras de negócio a um microserviço.
- Permite evoluir algoritmos (ML, mais limiares) sem redeploy do CLI.

**Porta:** `http://localhost:7002`

**Arranque:** `dotnet run --project ServicoAnalise`

---

### 4.6 Interface de Visualização (`InterfaceVisualizacao/`)

**O que faz:** Menu CLI para consultar medições, pedir análises (gRPC + gravação em `analises`) e rever histórico.

**Menu:**
1. Consultar medições (filtros opcionais; máx. 50 linhas)
2. Pedir nova análise (RPC)
3. Ver análises guardadas
0. Sair

**Porquê projeto separado do Servidor?**
- O servidor deve ficar sempre à escuta de gateways; misturar menu interativo bloquearia ou complicaria o processo.
- Ambos usam `ServidorService` + mesmo `DbPath` — consistência sem acoplamento de executáveis.

**Arranque:** `dotnet run --project InterfaceVisualizacao` (com RabbitMQ **não** obrigatório; gRPC Análise **sim** para opção 2)

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
- **Entrada:** `tipo_analise`, filtros, `repeated MedicaoDado medicoes` (a interface envia as linhas já lidas da BD)
- **Saída:** `resultado_json`

### 5.4 TCP Gateway → Servidor

Linha única por medição; resposta numa linha (`ACK` / `ERROR`). `ServerForwarder` reconecta se a ligação cair.

---

## 6. Padrões de design

| Padrão | Onde | Motivo |
|--------|------|--------|
| **Repository** | `SqliteMedicaoRepository`, `CsvSensorRegistoRepository` | Abstrai persistência; testável e substituível |
| **Strategy** | `IFormatParser` + JSON/XML/CSV | Novos formatos sem alterar gateway/serviço |
| **Factory** | `FormatParserFactory` | Escolhe parser pelo enum `FormatoDados` |
| **Pub/Sub** | RabbitMQ topic | Desacoplamento e routing por zona/tipo |
| **DIP** | Interfaces `IPreProcessador`, `IAnalisador`, `IMedicaoRepository` | Gateway e Interface dependem de abstrações |
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

#### RabbitMQ no Windows (sem Docker)

**Importante:** RabbitMQ 4.3 requer **Erlang 27** (não instalar Erlang 29 via winget — incompatível).

**Instalação inicial (uma vez, com UAC):**

```powershell
cd C:\Users\tomas\source\repos\SistemasDistribuidos\scripts
Set-ExecutionPolicy Bypass -Scope Process -Force
.\setup-rabbitmq.ps1
```

**Arrancar (antes de Gateway/Sensor):**

```powershell
cd C:\Users\tomas\source\repos\SistemasDistribuidos\scripts
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
3. Interface à parte:

```powershell
dotnet run --project InterfaceVisualizacao
```

Perfil alternativo **`TP2 - Servidor + Gateway + Sensor`**: sem serviços gRPC (útil só se não houver medições RPC/análise).

### Terminais manuais (ordem recomendada)

```powershell
# 0 — Broker
cd C:\Users\tomas\source\repos\SistemasDistribuidos\scripts
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

# 5 — Sensor
dotnet run --project Sensor -- S102 ZONA_ESCOLAR PM2.5,TEMP,RUIDO

# 6 — Interface (após existirem medições na BD)
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

### 9.4 Sensor — encerrar

```
> bye
```

```
[SENSOR] Encerrado.
```

---

### 9.5 Gateway — sensor não registado

Sensor `S999` não está em `sensores.csv`:

```
[GATEWAY] Recebido (medicao.ZONA_ESCOLAR.PM2.5): medicao de S999
[GATEWAY] Medição rejeitada — sensor inválido ou inativo.
```

---

### 9.6 Gateway — tipo não suportado

Sensor S102 não tem `HUM` na lista `[PM2.5,TEMP,RUIDO]` (medição `formato: NONE`):

```
[GATEWAY] Tipo HUM não suportado por S102.
```

---

### 9.7 Interface — consultar medições

```
Escolha: 1
ID do sensor (Enter = todos): S102
Tipo de dado (Enter = todos):
Zona (Enter = todas):
Data início (yyyy-MM-dd ou Enter):
Data fim (yyyy-MM-dd ou Enter):

--- Medições ---
2026-05-20 14:30 | S102 | ZONA_ESCOLAR | PM2.5 | 78
2026-05-20 14:31 | S102 | ZONA_ESCOLAR | TEMP | 30
Total mostrado: 2
```

---

### 9.8 Interface — análise de poluição

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

### 9.9 Interface — estatísticas sem dados

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

- Pré-processamento **multi-formato** (JSON, XML, CSV)
- **Três analisadores** (estatísticas, poluição, risco One Health)
- Interface com **filtros parametrizáveis** e histórico de análises na BD
- **Reconexão TCP** no gateway
- **Heartbeat** com desativação automática no CSV
- Scripts PowerShell para RabbitMQ no Windows
- Perfil Visual Studio **multi-startup**

---

## 12. Resolução de problemas

| Problema | Causa provável | Solução |
|----------|----------------|---------|
| `Connection refused` na porta 5672 | RabbitMQ parado | `.\scripts\start-rabbitmq.ps1` ou Docker |
| `StatusCode(Unavailable)` gRPC | Pré-proc ou Análise não arrancados | Arrancar projetos nas portas 7001/7002 **antes** dos clientes |
| Gateway não recebe mensagens | Zona diferente | Gateway: `ZONA_ESCOLAR`; sensor: mesma zona nos args |
| Medição rejeitada | Sensor ausente/inativo no CSV | Editar `Gateway/sensores.csv`; estado `ativo` |
| Interface sem dados | BD noutra pasta | Usar mesmo repo; ou `DB_PATH` apontando para `medicoes.db` na raiz |
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

*Documentação alinhada com o estado atual do repositório — Sistemas Distribuídos UTAD 2025/2026.*
