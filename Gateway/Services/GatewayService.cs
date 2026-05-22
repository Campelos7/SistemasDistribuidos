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

    private async Task ProcessarMensagemAsync(MensagemPubSub msg, string routingKey)
    {
        Console.WriteLine($"[GATEWAY] Recebido ({routingKey}): {msg.Tipo} de {msg.SensorId}");

        var sensores = _sensorRepository.CarregarTodos();

        switch (msg.Tipo.ToLowerInvariant())
        {
            case "registo":
                TratarRegisto(msg, sensores);
                break;
            case "heartbeat":
                TratarHeartbeat(msg, sensores);
                break;
            case "medicao":
                await TratarMedicaoAsync(msg, sensores);
                break;
        }
    }

    private bool ZonaCompativel(MensagemPubSub msg) =>
        msg.Zona.Equals(_config.ZonaGerida, StringComparison.OrdinalIgnoreCase);

    private void TratarRegisto(MensagemPubSub msg, IReadOnlyDictionary<string, SensorRegisto> sensores)
    {
        if (!ZonaCompativel(msg))
        {
            Console.WriteLine($"[GATEWAY] Registo ignorado — zona {msg.Zona} não é gerida por este gateway.");
            return;
        }

        if (!sensores.TryGetValue(msg.SensorId, out var registo))
        {
            Console.WriteLine($"[GATEWAY] Sensor {msg.SensorId} não registado no CSV.");
            return;
        }

        if (!registo.EstaOperacional)
        {
            Console.WriteLine($"[GATEWAY] Sensor {msg.SensorId} em estado {registo.Estado}.");
            return;
        }

        _sensorRepository.AtualizarUltimaSincronizacao(msg.SensorId, DateTime.Now);
        Console.WriteLine($"[GATEWAY] Registo aceite: {msg.SensorId}");
    }

    private void TratarHeartbeat(MensagemPubSub msg, IReadOnlyDictionary<string, SensorRegisto> sensores)
    {
        if (sensores.ContainsKey(msg.SensorId))
            _sensorRepository.AtualizarUltimaSincronizacao(msg.SensorId, DateTime.Now);
    }

    private async Task TratarMedicaoAsync(MensagemPubSub msg, IReadOnlyDictionary<string, SensorRegisto> sensores)
    {
        if (!ZonaCompativel(msg))
            return;

        if (!sensores.TryGetValue(msg.SensorId, out var registo) || !registo.EstaOperacional)
        {
            Console.WriteLine($"[GATEWAY] Medição rejeitada — sensor inválido ou inativo.");
            return;
        }

        string tipo = msg.TipoDado ?? "DESCONHECIDO";
        if (!registo.SuportaTipo(tipo) && msg.Formato == "NONE")
        {
            Console.WriteLine($"[GATEWAY] Tipo {tipo} não suportado por {msg.SensorId}.");
            return;
        }

        try
        {
            var medicao = CriarMedicao(msg);

            // Validar que o sensorId do payload corresponde ao publicador (anti-spoofing)
            if (!medicao.SensorId.Equals(msg.SensorId, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[GATEWAY] Medição rejeitada — sensorId no payload ({medicao.SensorId}) difere do publicador ({msg.SensorId}).");
                return;
            }

            // Validar tipo suportado após parsing (cobre formatos JSON/XML/CSV)
            if (!registo.SuportaTipo(medicao.TipoDado))
            {
                Console.WriteLine($"[GATEWAY] Tipo {medicao.TipoDado} não suportado por {msg.SensorId}.");
                return;
            }

            var processada = await _preProcessador.ProcessarAsync(medicao);
            bool ok = await _forwarder.EnviarMedicaoAsync(processada);
            if (ok)
                _sensorRepository.AtualizarUltimaSincronizacao(msg.SensorId, DateTime.Now);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GATEWAY] Erro ao processar medição: {ex.Message}");
        }
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
