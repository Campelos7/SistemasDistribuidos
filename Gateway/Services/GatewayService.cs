using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;
using Common.Serialization;
using Gateway.RpcClient;
using Gateway.ServerConnection;
using Gateway.Subscriber;

namespace Gateway.Services;

/// <summary>
/// Orquestra subscrição Pub/Sub, validação CSV, RPC de pré-processamento e envio TCP ao servidor.
/// Todas as dependências são injetadas via construtor (DIP + SRP).
/// </summary>
public class GatewayService
{
    private readonly GatewayConfig _config;
    private readonly ISensorRegistoRepository _sensorRepository;
    private readonly IPreProcessador _preProcessador;
    private readonly ServerForwarder _forwarder;
    private readonly RabbitMqSubscriber _subscriber;
    private readonly HeartbeatMonitor _heartbeatMonitor;
    private readonly FormatParserFactory _parserFactory;

    public GatewayService(
        GatewayConfig config,
        ISensorRegistoRepository sensorRepository,
        IPreProcessador preProcessador,
        ServerForwarder forwarder,
        RabbitMqSubscriber subscriber,
        HeartbeatMonitor heartbeatMonitor,
        FormatParserFactory parserFactory)
    {
        _config = config;
        _sensorRepository = sensorRepository;
        _preProcessador = preProcessador;
        _forwarder = forwarder;
        _subscriber = subscriber;
        _heartbeatMonitor = heartbeatMonitor;
        _parserFactory = parserFactory;
    }

    /// <summary>
    /// Arranca o gateway: subscrição, monitorização e processamento de mensagens.
    /// </summary>
    public void Iniciar()
    {
        _subscriber.MensagemRecebida += ProcessarMensagemAsync;
        _subscriber.SubscreverZona(_config.ZonaGerida);
        _heartbeatMonitor.Iniciar();

        Console.WriteLine($"[GATEWAY] Ativo para zona {_config.ZonaGerida}. Pressione ENTER para sair.");
        Console.ReadLine();
    }

    /// <summary>
    /// Processa mensagem Pub/Sub. Devolve <c>true</c> quando a operação terminou (ACK RabbitMQ);
    /// <c>false</c> quando falhou de forma transitória e a mensagem deve ser reencaminhada (NACK).
    /// </summary>
    private async Task<bool> ProcessarMensagemAsync(MensagemPubSub msg, string routingKey)
    {
        Console.WriteLine($"[GATEWAY] Recebido ({routingKey}): {msg.Tipo} de {msg.SensorId}");

        var sensores = _sensorRepository.CarregarTodos();

        return msg.Tipo.ToLowerInvariant() switch
        {
            "registo" => TratarRegisto(msg, sensores),
            "heartbeat" => TratarHeartbeat(msg, sensores),
            "medicao" => await TratarMedicaoAsync(msg, sensores),
            _ => true
        };
    }

    private bool ZonaCompativel(MensagemPubSub msg) =>
        msg.Zona.Equals(_config.ZonaGerida, StringComparison.OrdinalIgnoreCase);

    private bool TratarRegisto(MensagemPubSub msg, IReadOnlyDictionary<string, SensorRegisto> sensores)
    {
        if (!ZonaCompativel(msg))
        {
            Console.WriteLine($"[GATEWAY] Registo ignorado — zona {msg.Zona} não é gerida por este gateway.");
            return true;
        }

        if (!sensores.TryGetValue(msg.SensorId, out var registo))
        {
            Console.WriteLine($"[GATEWAY] Sensor {msg.SensorId} não registado no CSV.");
            return true;
        }

        if (!registo.EstaOperacional)
        {
            Console.WriteLine($"[GATEWAY] Sensor {msg.SensorId} em estado {registo.Estado}.");
            return true;
        }

        _sensorRepository.AtualizarUltimaSincronizacao(msg.SensorId, DateTime.Now);
        Console.WriteLine($"[GATEWAY] Registo aceite: {msg.SensorId}");
        return true;
    }

    private bool TratarHeartbeat(MensagemPubSub msg, IReadOnlyDictionary<string, SensorRegisto> sensores)
    {
        if (sensores.ContainsKey(msg.SensorId))
            _sensorRepository.AtualizarUltimaSincronizacao(msg.SensorId, DateTime.Now);
        return true;
    }

    /// <summary>
    /// Pré-processa via gRPC e envia ao Servidor TCP. Só devolve <c>true</c> após <c>ACK</c> do Servidor.
    /// Excepções de rede/gRPC propagam-se para o consumidor RabbitMQ (NACK + requeue).
    /// </summary>
    private async Task<bool> TratarMedicaoAsync(MensagemPubSub msg, IReadOnlyDictionary<string, SensorRegisto> sensores)
    {
        if (!ZonaCompativel(msg))
            return true;

        if (!sensores.TryGetValue(msg.SensorId, out var registo) || !registo.EstaOperacional)
        {
            Console.WriteLine($"[GATEWAY] Medição rejeitada — sensor inválido ou inativo.");
            return true;
        }

        string tipo = msg.TipoDado ?? "DESCONHECIDO";
        if (!registo.SuportaTipo(tipo) && msg.Formato == "NONE")
        {
            Console.WriteLine($"[GATEWAY] Tipo {tipo} não suportado por {msg.SensorId}.");
            return true;
        }

        var medicao = CriarMedicao(msg);

        if (!medicao.SensorId.Equals(msg.SensorId, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[GATEWAY] Medição rejeitada — sensorId no payload ({medicao.SensorId}) difere do publicador ({msg.SensorId}).");
            return true;
        }

        if (!registo.SuportaTipo(medicao.TipoDado))
        {
            Console.WriteLine($"[GATEWAY] Tipo {medicao.TipoDado} não suportado por {msg.SensorId}.");
            return true;
        }

        var processada = await _preProcessador.ProcessarAsync(medicao);
        bool ackServidor = await _forwarder.EnviarMedicaoAsync(processada);
        if (!ackServidor)
        {
            Console.WriteLine("[GATEWAY] Servidor não confirmou ACK — mensagem será reencaminhada.");
            return false;
        }

        _sensorRepository.AtualizarUltimaSincronizacao(msg.SensorId, DateTime.Now);
        return true;
    }

    /// <summary>
    /// Constrói uma Medicao a partir da mensagem Pub/Sub, usando o parser adequado ao formato.
    /// </summary>
    private Medicao CriarMedicao(MensagemPubSub msg)
    {
        var formato = _parserFactory.ParseFormato(msg.Formato);
        DateTime ts = DateTime.TryParse(msg.Timestamp, out var parsed) ? parsed : DateTime.Now;

        if (formato != FormatoDados.None && !string.IsNullOrWhiteSpace(msg.Payload))
        {
            return _parserFactory.Obter(formato).Parse(msg.Payload, msg.SensorId, msg.Zona);
        }

        return new Medicao(
            msg.SensorId,
            msg.Zona,
            msg.TipoDado ?? "DESCONHECIDO",
            msg.Valor ?? 0,
            ts,
            formato,
            msg.Payload);
    }
}
