namespace Common.Models;

/// <summary>
/// Mensagem publicada no RabbitMQ entre sensores e gateways.
/// Serializada em JSON no corpo da mensagem AMQP.
/// </summary>
public class MensagemPubSub
{
    /// <summary>Tipo: medicao, heartbeat ou registo.</summary>
    public string Tipo { get; set; } = "medicao";

    public string SensorId { get; set; } = string.Empty;
    public string Zona { get; set; } = string.Empty;
    public string? TipoDado { get; set; }
    public double? Valor { get; set; }
    public string? Timestamp { get; set; }

    /// <summary>Formato dos dados: NONE, JSON, XML, CSV.</summary>
    public string Formato { get; set; } = "NONE";

    /// <summary>Payload alternativo quando o formato não é NONE.</summary>
    public string? Payload { get; set; }

    public List<string>? TiposSuportados { get; set; }
}
