# Documentação do TP2 — Monitorização Urbana One Health

## 1. O que é este projeto?

Este projeto implementa o **Trabalho Prático 2** da unidade de Sistemas Distribuídos (UTAD 2025/2026). Evolui o sistema do TP1 (sensores → gateway → servidor via TCP) para uma **arquitetura distribuída** com:

| Mecanismo | Onde é usado | Tecnologia |
|-----------|--------------|------------|
| **Pub/Sub** | Sensor → Gateway | RabbitMQ (exchange topic) |
| **RPC** | Gateway → Pré-processamento | gRPC (porta 7001) |
| **RPC** | Servidor/Interface → Análise | gRPC (porta 7002) |
| **TCP** | Gateway → Servidor | Sockets (porta 6000, protocolo TP1) |
| **BD** | Servidor + Interface | SQLite (`medicoes.db`) |
| **CLI** | Utilizador | `InterfaceVisualizacao` |

O objetivo é simular monitorização ambiental urbana (temperatura, humidade, PM2.5, ruído, etc.) no paradigma **One Health**, com código orientado a objetos, separação de responsabilidades e padrões de design.

---

## 2. Arquitetura geral

```
┌─────────────┐     Pub/Sub      ┌──────────────┐    gRPC     ┌──────────────────┐
│   Sensor    │ ───────────────► │   Gateway    │ ──────────► │ Pré-Processamento│
│ (publica)   │    RabbitMQ      │ (subscreve)  │             │   (porta 7001)   │
└─────────────┘                  └──────┬───────┘             └──────────────────┘
                                        │ TCP DATA|...
                                        ▼
                                 ┌──────────────┐    gRPC     ┌──────────────────┐
                                 │   Servidor   │ ◄────────── │ Serviço Análise  │
                                 │ (porta 6000) │             │   (porta 7002)   │
                                 └──────┬───────┘             └──────────────────┘
                                        │ SQLite
                                        ▼
                                 ┌──────────────────┐
                                 │ Interface CLI    │
                                 │ (consultas +   │
                                 │  pedidos análise)│
                                 └──────────────────┘
```

---

## 3. Estrutura da solução

```
SistemasDistribuidos/
├── Common/                    # Biblioteca partilhada (modelos, interfaces, BD, protos)
├── Sensor/                    # Publica no RabbitMQ
├── Gateway/                   # Subscreve, RPC pré-proc, envia TCP
├── PreProcessamento/          # Serviço gRPC
├── TrabalhoPratico/           # Servidor principal (projeto "Servidor")
├── ServicoAnalise/            # Serviço gRPC de análises
├── InterfaceVisualizacao/     # CLI para consultas e análises
└── DOCUMENTACAO_TP2.md        # Este ficheiro
```

### Porquê esta estrutura?

- **Common**: evita duplicação; contratos (`IPreProcessador`, `IAnalisador`, `IMedicaoRepository`) e modelos (`Medicao`) são partilhados — **DIP** (Dependency Inversion).
- **Um projeto por processo**: cada componente corre independentemente, como num sistema distribuído real.
- **Protos gRPC em Common**: cliente e servidor usam as mesmas definições `.proto` — contrato formal de RPC.

---

## 4. Componentes em detalhe

### 4.1 Sensor (`Sensor/`)

**O que faz:** Simula um sensor urbano. Publica medições, heartbeats e registo no RabbitMQ.

**Como faz:**
1. Lê configuração (`SensorId`, `Zona`, tipos) dos argumentos ou valores por defeito.
2. Liga ao RabbitMQ e publica mensagem de **registo**.
3. Thread em background envia **heartbeat** a cada 30 s.
4. Interface CLI: `data <tipo> <valor>`, `datajson <json>`, `bye`.

**Routing keys (tópicos):**
- Medições: `medicao.{ZONA}.{TIPO}` — ex.: `medicao.ZONA_ESCOLAR.PM2.5`
- Heartbeat: `heartbeat.{ZONA}.{SENSOR_ID}`
- Registo: `registo.{ZONA}.{SENSOR_ID}`

**Porquê Pub/Sub em vez de TCP direto ao gateway?**
- Desacoplamento: o sensor não precisa de saber o IP do gateway.
- Escalabilidade: vários gateways podem subscrever padrões diferentes.
- Requisito explícito do enunciado do TP2.

**Ficheiros principais:**
| Ficheiro | Função |
|----------|--------|
| `SensorConfig.cs` | Configuração do sensor |
| `Publisher/RabbitMqPublisher.cs` | Publicação AMQP |
| `SensorApp.cs` | Orquestra CLI e mensagens |
| `Program.cs` | Entry point |

---

### 4.2 Gateway (`Gateway/`)

**O que faz:** Intermediário entre sensores (via broker) e servidor. Valida sensores no CSV, chama pré-processamento RPC, encaminha dados.

**Como faz:**
1. Subscreve padrões da sua zona no exchange `monitorizacao.urbana`.
2. Para cada medição: valida no `sensores.csv` → RPC pré-processamento → TCP ao servidor.
3. `HeartbeatMonitor` verifica sensores sem comunicação (>90 s) e marca **desativado**.

**Ficheiro CSV** (`sensores.csv`):
```
S102:ativo:ZONA_ESCOLAR:[PM2.5,TEMP,RUIDO]:2026-05-17T10:00:00
```

**Porquê manter CSV do TP1?**
- Continuidade com validação de sensores registados e estados.
- Ficheiro partilhado entre execuções do gateway.

**Ficheiros principais:**
| Ficheiro | Função |
|----------|--------|
| `GatewayService.cs` | Orquestração do fluxo |
| `Subscriber/RabbitMqSubscriber.cs` | Consumidor RabbitMQ |
| `RpcClient/PreProcessamentoGrpcClient.cs` | Cliente gRPC |
| `ServerConnection/ServerForwarder.cs` | Cliente TCP para servidor |
| `Services/HeartbeatMonitor.cs` | Timeout de heartbeats |

**Arranque:** `dotnet run --project Gateway -- ZONA_ESCOLAR`

---

### 4.3 Pré-Processamento (`PreProcessamento/`)

**O que faz:** Serviço RPC que uniformiza dados antes da agregação.

**Operações:**
- **Conversão de escalas:** temperatura >50 assume Fahrenheit → Celsius; humidade 0–1 → percentagem.
- **Parsing multi-formato:** JSON, XML, CSV via Strategy (`JsonFormatParser`, `XmlFormatParser`, `CsvFormatParser`) + `FormatParserFactory`.

**Porquê gRPC?**
- Contrato tipado (`.proto`), performance, padrão industrial para RPC em .NET.

**Porta:** `http://localhost:7001` (HTTP/2)

**Arranque:** `dotnet run --project PreProcessamento`

---

### 4.4 Servidor (`TrabalhoPratico/` — assembly `Servidor`)

**O que faz:** Recebe medições dos gateways por TCP, persiste em SQLite, expõe lógica para análises.

**Protocolo TCP (herdado do TP1):**
```
DATA|sensor_id|zona|tipo_dado|valor|timestamp
→ Resposta: ACK ou ERROR
```

**Base de dados** (`medicoes.db`):
- Tabela `medicoes`: medições ambientais.
- Tabela `analises`: resultados JSON de análises RPC.

**Porquê SQLite?**
- Sem servidor externo; adequado ao âmbito académico; já usado no TP1.

**Arranque:** `dotnet run --project TrabalhoPratico`

---

### 4.5 Serviço de Análise (`ServicoAnalise/`)

**O que faz:** Análises especializadas invocadas remotamente.

| Tipo | Classe | Descrição |
|------|--------|-----------|
| `ESTATISTICAS` | `EstatisticasAnalyzer` | Média, min, max, desvio padrão |
| `POLUICAO` | `PoluicaoDetector` | Alertas PM2.5, PM10, ruído acima de limiares |
| `RISCO` | `RiscoPredictor` | Índice de risco 0–100 para saúde pública |

**Porta:** `http://localhost:7002`

**Arranque:** `dotnet run --project ServicoAnalise`

---

### 4.6 Interface de Visualização (`InterfaceVisualizacao/`)

**O que faz:** CLI para o utilizador consultar medições, pedir análises parametrizadas e ver resultados guardados.

**Menu:**
1. Consultar medições (filtros: sensor, tipo, zona, datas)
2. Pedir nova análise (dispara RPC + grava resultado)
3. Ver análises guardadas
0. Sair

**Porquê projeto separado?**
- Separa a UI do processo servidor que deve ficar sempre à escuta de gateways.

**Arranque:** `dotnet run --project InterfaceVisualizacao`

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
  "timestamp": "2026-05-17T10:15:00",
  "formato": "NONE"
}
```

### 5.2 RPC Pré-processamento (gRPC)

- **Serviço:** `PreProcessamentoService.ProcessarMedicao`
- **Entrada:** sensor_id, zona, tipo_dado, valor, timestamp, formato, payload
- **Saída:** medição normalizada ou erro

### 5.3 RPC Análise (gRPC)

- **Serviço:** `AnaliseService.ExecutarAnalise`
- **Entrada:** tipo_analise, filtros, lista de medições
- **Saída:** `resultado_json`

### 5.4 TCP Gateway → Servidor

Mantém compatibilidade com TP1: `DATA|...|ACK`

---

## 6. Padrões de design utilizados

| Padrão | Onde | Motivo |
|--------|------|--------|
| **Repository** | `SqliteMedicaoRepository` | Abstrai acesso à BD |
| **Strategy** | `IFormatParser` + JSON/XML/CSV | Formatos intermutáveis |
| **Factory** | `FormatParserFactory` | Seleciona parser |
| **Pub/Sub** | RabbitMQ | Desacoplamento sensor/gateway |
| **DI manual** | `Program.cs` de cada projeto | Injeta dependências no construtor |
| **SRP** | Classes por responsabilidade | Uma razão para mudar |

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
| `DB_PATH` | medicoes.db | Caminho SQLite |

---

## 8. Como executar (ordem recomendada)

### Pré-requisitos
- .NET 8 SDK
- RabbitMQ em execução (ver secção **Instalar RabbitMQ no Windows** abaixo)

#### Instalar RabbitMQ no Windows (sem Docker)

**Importante:** RabbitMQ 4.3 precisa de **Erlang 27** (não instales Erlang 29 via winget — é incompatível).

**Instalação inicial (uma vez, com UAC):**
```powershell
cd C:\Users\tomas\source\repos\SistemasDistribuidos\scripts
Set-ExecutionPolicy Bypass -Scope Process -Force
.\setup-rabbitmq.ps1
```
(Clica **Sim** no UAC e espera na janela que abrir.)

**Arrancar RabbitMQ (sempre que fores trabalhar no TP2):**
```powershell
cd C:\Users\tomas\source\repos\SistemasDistribuidos\scripts
.\start-rabbitmq.ps1
```

**Parar RabbitMQ:**
```powershell
.\stop-rabbitmq.ps1
```

> O hostname do PC com acentos (ex.: Tomás) impede o serviço Windows de arrancar. Os scripts usam `rabbit@localhost` — solução estável para o TP2.

Interface web: http://localhost:15672 (`guest` / `guest`)

**Alternativa Docker** (se tiveres Docker Desktop):
```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

### Visual Studio — botão Iniciar (recomendado)

1. **Primeiro:** `.\scripts\start-rabbitmq.ps1` (RabbitMQ não arranca pelo Visual Studio)
2. No Visual Studio, ao lado de **Iniciar**, escolhe o perfil **`TP2 - Sistema Completo`**
3. Clica **Iniciar** — abre 5 consolas automaticamente (Pré-proc, Análise, Servidor, Gateway, Sensor)

A **Interface de Visualização** corre à parte (pode estar o sistema a correr ao mesmo tempo):
```powershell
dotnet run --project InterfaceVisualizacao
```

### Terminais (alternativa manual)

```powershell
# 0 — RabbitMQ (obrigatório antes do Gateway/Sensor)
cd scripts
.\start-rabbitmq.ps1
cd ..

# 1 — Pré-processamento gRPC
dotnet run --project PreProcessamento

# 2 — Serviço de análise gRPC
dotnet run --project ServicoAnalise

# 3 — Servidor TCP + SQLite
dotnet run --project TrabalhoPratico

# 4 — Gateway (zona escolar)
dotnet run --project Gateway -- ZONA_ESCOLAR

# 5 — Sensor
dotnet run --project Sensor -- S102 ZONA_ESCOLAR PM2.5,TEMP,RUIDO

# 6 — Interface (opcional, após haver dados)
dotnet run --project InterfaceVisualizacao
```

### Teste rápido no sensor
```
> data PM2.5 78
> data TEMP 86
> bye
```

A temperatura 86 será convertida de Fahrenheit para Celsius no pré-processamento.

### Teste JSON no sensor
```
> datajson {"sensorId":"S102","zona":"ZONA_ESCOLAR","tipoDado":"HUM","valor":0.65,"timestamp":"2026-05-17T12:00:00"}
```

---

## 9. Diferenças face ao TP1 (melhorias de engenharia)

| TP1 | TP2 |
|-----|-----|
| Tudo `static` num `Program.cs` | Classes instanciadas, injeção no construtor |
| Sem interfaces | `IPreProcessador`, `IAnalisador`, `IMedicaoRepository`, etc. |
| TCP sensor→gateway | RabbitMQ Pub/Sub |
| Sem RPC | gRPC pré-proc + análise |
| Parsing `Split('|')` sem modelo | Classe `Medicao` com validação |
| God classes | Separação por pastas e SRP |

---

## 10. Ficheiros de código comentados

Todo o código C# inclui:
- Comentários `/// <summary>` em classes e métodos públicos
- Comentários inline onde a lógica não é óbvia
- Nomes em português no domínio (Medicao, Zona, SensorRegisto)

Para navegar, comece por:
1. `Common/Models/Medicao.cs` — entidade central
2. `Gateway/Services/GatewayService.cs` — fluxo principal do gateway
3. `TrabalhoPratico/ServidorService.cs` — lógica do servidor
4. `Common/Protos/*.proto` — contratos RPC

---

## 11. Valorização (extras implementados)

- **Multi-formato:** JSON, XML, CSV no pré-processamento
- **Três tipos de análise:** estatísticas, poluição, risco
- **Interface CLI** com filtros parametrizáveis
- **Persistência de análises** na mesma BD

---

## 12. Resolução de problemas

| Problema | Solução |
|----------|---------|
| `Connection refused` RabbitMQ | Verificar Docker/serviço na porta 5672 |
| gRPC unavailable | Arrancar PreProcessamento/ServicoAnalise antes dos clientes |
| Gateway não recebe mensagens | Zona do gateway deve coincidir com a do sensor |
| Sensor não registado | Adicionar linha em `Gateway/sensores.csv` |
| BD vazia na interface | Usar o mesmo `DB_PATH`; servidor cria `medicoes.db` na pasta de execução |

---

*Documentação gerada para o TP2 — Sistemas Distribuídos UTAD 2025/2026.*
