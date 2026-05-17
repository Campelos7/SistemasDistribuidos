using System.Text;
using System.Text.Json;
using Common.Config;
using Common.Messaging;
using Common.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gateway.Subscriber;

/// <summary>
/// Subscreve tópicos RabbitMQ da zona gerida pelo gateway.
/// </summary>
public class RabbitMqSubscriber : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _canal;
    private readonly RabbitMqConnectionFactory _factory = new();

    public event Func<MensagemPubSub, string, Task>? MensagemRecebida;

    public RabbitMqSubscriber()
    {
        _connection = _factory.ObterLigacao();
        _canal = _connection.CreateModel();
        _factory.DeclararExchange(_canal);
    }

    /// <summary>
    /// Cria fila exclusiva e liga aos padrões de routing da zona.
    /// </summary>
    public void SubscreverZona(string zona)
    {
        string queueName = _canal.QueueDeclare().QueueName;

        string[] patterns =
        {
            RoutingKeys.MedicaoZona(zona),
            RoutingKeys.HeartbeatZona(zona),
            RoutingKeys.RegistoZona(zona)
        };

        foreach (string pattern in patterns)
            _canal.QueueBind(queueName, AppSettings.ExchangeMonitorizacao, pattern);

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
