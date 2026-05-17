using System.Text;
using System.Text.Json;
using Common.Config;
using Common.Messaging;
using Common.Models;
using RabbitMQ.Client;

namespace Sensor.Publisher;

/// <summary>
/// Publica mensagens no RabbitMQ (padrão Pub/Sub do TP2).
/// </summary>
public class RabbitMqPublisher : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _canal;
    private readonly RabbitMqConnectionFactory _factory = new();

    public RabbitMqPublisher()
    {
        _connection = _factory.ObterLigacao();
        _canal = _connection.CreateModel();
        _factory.DeclararExchange(_canal);
    }

    /// <summary>
    /// Publica uma mensagem no exchange topic com a routing key indicada.
    /// </summary>
    public void Publicar(string routingKey, MensagemPubSub mensagem)
    {
        string json = JsonSerializer.Serialize(mensagem);
        var props = _canal.CreateBasicProperties();
        props.ContentType = "application/json";
        props.DeliveryMode = 2; // persistente

        byte[] body = Encoding.UTF8.GetBytes(json);
        _canal.BasicPublish(AppSettings.ExchangeMonitorizacao, routingKey, props, body);
    }

    public void Dispose()
    {
        _canal.Close();
        _connection.Close();
    }
}
