using System.Text;
using System.Text.Json;
using Common.Messaging;
using Common.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gateway.Subscriber;

/// <summary>
/// Subscreve tópicos RabbitMQ da zona gerida pelo gateway.
/// Recebe a factory, exchange e routing keys via construtor (DIP).
/// </summary>
public class RabbitMqSubscriber : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _canal;
    private readonly string _exchangeName;
    private readonly RoutingKeys _routingKeys;

    public event Func<MensagemPubSub, string, Task>? MensagemRecebida;

    public RabbitMqSubscriber(RabbitMqConnectionFactory factory, string exchangeName, RoutingKeys routingKeys)
    {
        _exchangeName = exchangeName;
        _routingKeys = routingKeys;
        _connection = factory.ObterLigacao();
        _canal = _connection.CreateModel();
        factory.DeclararExchange(_canal);
    }

    /// <summary>
    /// Cria fila exclusiva e liga aos padrões de routing da zona.
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

        var consumer = new AsyncEventingBasicConsumer(_canal);
        consumer.Received += async (_, ea) =>
        {
            string json = Encoding.UTF8.GetString(ea.Body.ToArray());
            var msg = JsonSerializer.Deserialize<MensagemPubSub>(json);
            if (msg != null && MensagemRecebida != null)
                await MensagemRecebida(msg, ea.RoutingKey);
            _canal.BasicAck(ea.DeliveryTag, false);
        };

        _canal.BasicConsume(queueName, autoAck: false, consumer);
        Console.WriteLine($"[GATEWAY] Subscrito à zona {zona} (patterns: {string.Join(", ", patterns)})");
    }

    public void Dispose()
    {
        _canal.Close();
        _connection.Close();
    }
}
