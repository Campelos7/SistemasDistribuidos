using System.Text;
using System.Text.Json;
using Common.Messaging;
using Common.Models;
using RabbitMQ.Client;

namespace Sensor.Publisher;

/// <summary>
/// Publica mensagens no RabbitMQ (padrão Pub/Sub do TP2).
/// Recebe a factory e o nome do exchange via construtor (DIP).
/// </summary>
public class RabbitMqPublisher : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _canal;
    private readonly string _exchangeName;

    public RabbitMqPublisher(RabbitMqConnectionFactory factory, string exchangeName)
    {
        _exchangeName = exchangeName;
        _connection = factory.ObterLigacao();
        _canal = _connection.CreateModel();
        factory.DeclararExchange(_canal);
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
        _canal.BasicPublish(_exchangeName, routingKey, props, body);
    }

    public void Dispose()
    {
        _canal.Close();
        _connection.Close();
    }
}
