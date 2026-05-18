using Common.Config;
using RabbitMQ.Client;

namespace Common.Messaging;

/// <summary>
/// Cria ligações ao broker RabbitMQ com as definições da aplicação.
/// Inclui lógica de retry para resiliência da conexão.
/// </summary>
public class RabbitMqConnectionFactory
{
    private readonly AppSettings _settings;
    private IConnection? _connection;

    public RabbitMqConnectionFactory(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Obtém ou cria uma ligação partilhada ao broker com retry automático.
    /// </summary>
    public IConnection ObterLigacao()
    {
        if (_connection is { IsOpen: true })
            return _connection;

        var factory = new ConnectionFactory
        {
            HostName = _settings.RabbitHost,
            UserName = _settings.RabbitUser,
            Password = _settings.RabbitPass,
            DispatchConsumersAsync = true
        };

        for (int tentativa = 1; tentativa <= 3; tentativa++)
        {
            try
            {
                _connection = factory.CreateConnection();
                return _connection;
            }
            catch (Exception ex) when (tentativa < 3)
            {
                int delayMs = tentativa * 2000;
                Console.WriteLine($"[RABBIT] Tentativa {tentativa}/3 falhou: {ex.Message}. A tentar novamente em {delayMs}ms...");
                Thread.Sleep(delayMs);
            }
        }

        // Última tentativa — deixa a exceção propagar se falhar
        _connection = factory.CreateConnection();
        return _connection;
    }

    /// <summary>
    /// Declara o exchange topic usado pelo sistema.
    /// </summary>
    public void DeclararExchange(IModel canal)
    {
        canal.ExchangeDeclare(
            exchange: _settings.ExchangeMonitorizacao,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
    }
}
