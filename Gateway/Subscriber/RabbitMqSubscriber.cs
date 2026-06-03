using System.Text;
using System.Text.Json;
using Common.Messaging;
using Common.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gateway.Subscriber;

/// <summary>
/// Serviço de consumo RabbitMQ do Gateway (reconhecimento manual, semântica At-Least-Once).
/// </summary>
/// <remarks>
/// <para>
/// Com <c>autoAck: false</c>, a mensagem só é removida da fila após confirmação explícita.
/// O handler invoca <see cref="IModel.BasicAck"/> apenas quando o processamento downstream
/// (pré-processamento gRPC + envio TCP com resposta <c>ACK</c> do Servidor) conclui com sucesso.
/// </para>
/// <para>
/// Falhas transitórias (rede, gRPC indisponível, Servidor TCP sem resposta) originam
/// <see cref="IModel.BasicNack"/> com <c>requeue: true</c>, garantindo reentrega após crash
/// ou falha do Gateway — semântica <b>At-Least-Once</b>.
/// </para>
/// <para>
/// <see cref="IModel.BasicQos"/> com prefetch &gt; 1 permite processar várias mensagens em
/// paralelo; as confirmações usam <c>deliveryTag</c> individuais sob lock no canal (thread-safe).
/// </para>
/// </remarks>
public class RabbitMqSubscriber : IDisposable
{
    private const ushort PrefetchCount = 10;

    private readonly IConnection _connection;
    private readonly IModel _canal;
    private readonly string _exchangeName;
    private readonly RoutingKeys _routingKeys;
    private readonly object _ackLock = new();

    /// <summary>
    /// Processa a mensagem deserializada. Devolve <c>true</c> para <c>BasicAck</c>,
    /// <c>false</c> para <c>BasicNack(requeue: true)</c>.
    /// </summary>
    public event Func<MensagemPubSub, string, Task<bool>>? MensagemRecebida;

    public RabbitMqSubscriber(RabbitMqConnectionFactory factory, string exchangeName, RoutingKeys routingKeys)
    {
        _exchangeName = exchangeName;
        _routingKeys = routingKeys;
        _connection = factory.ObterLigacao();
        _canal = _connection.CreateModel();
        factory.DeclararExchange(_canal);
    }

    /// <summary>
    /// Cria fila exclusiva, liga aos padrões da zona e consome com reconhecimento manual.
    /// </summary>
    public void SubscreverZona(string zona)
    {
        string queueName = _canal.QueueDeclare().QueueName;

        string[] patterns =
        {
            _routingKeys.MedicaoZona(zona),
            _routingKeys.HeartbeatZona(zona),
            _routingKeys.RegistoZona(zona)
        };

        foreach (string pattern in patterns)
            _canal.QueueBind(queueName, _exchangeName, pattern);

        _canal.BasicQos(prefetchSize: 0, prefetchCount: PrefetchCount, global: false);

        var consumer = new AsyncEventingBasicConsumer(_canal);
        consumer.Received += OnMensagemRecebidaAsync;

        _canal.BasicConsume(queueName, autoAck: false, consumer);
        Console.WriteLine($"[GATEWAY] Subscrito à zona {zona} (manual ack, prefetch {PrefetchCount}).");
    }

    private async Task OnMensagemRecebidaAsync(object sender, BasicDeliverEventArgs ea)
    {
        ulong deliveryTag = ea.DeliveryTag;

        try
        {
            string json = Encoding.UTF8.GetString(ea.Body.ToArray());
            MensagemPubSub? msg = JsonSerializer.Deserialize<MensagemPubSub>(json);

            if (msg == null)
            {
                Console.WriteLine("[GATEWAY] Mensagem RabbitMQ inválida (JSON) — a descartar.");
                ConfirmarEntrega(deliveryTag, sucesso: true);
                return;
            }

            if (MensagemRecebida == null)
            {
                ConfirmarEntrega(deliveryTag, sucesso: false);
                return;
            }

            bool sucesso = await MensagemRecebida(msg, ea.RoutingKey);
            ConfirmarEntrega(deliveryTag, sucesso);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GATEWAY] Falha no processamento — mensagem reencaminhada: {ex.Message}");
            ConfirmarEntrega(deliveryTag, sucesso: false);
        }
    }

    /// <summary>
    /// Confirma ou rejeita uma entrega. O canal RabbitMQ não é thread-safe — serializado por lock.
    /// </summary>
    private void ConfirmarEntrega(ulong deliveryTag, bool sucesso)
    {
        lock (_ackLock)
        {
            if (sucesso)
                _canal.BasicAck(deliveryTag, multiple: false);
            else
                _canal.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
    }

    public void Dispose()
    {
        _canal.Close();
        _connection.Close();
    }
}
