using Common.Config;
using RabbitMQ.Client;

namespace Common.Messaging;

/// <summary>
/// Cria ligações ao broker RabbitMQ com as definições da aplicação.
/// </summary>
public class RabbitMqConnectionFactory
{
    private IConnection? _connection;

    /// <summary>
    /// Obtém ou cria uma ligação partilhada ao broker.
    /// </summary>
    public IConnection ObterLigacao()
    {
        if (_connection is { IsOpen: true })
            return _connection;

        var factory = new ConnectionFactory
        {
            HostName = AppSettings.RabbitHost,
            UserName = AppSettings.RabbitUser,
            Password = AppSettings.RabbitPass,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        return _connection;
    }

    /// <summary>
    /// Declara o exchange topic usado pelo sistema.
    /// </summary>
    public void DeclararExchange(IModel canal)
    {
        canal.ExchangeDeclare(
            exchange: AppSettings.ExchangeMonitorizacao,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
    }
}
