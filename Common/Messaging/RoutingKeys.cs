namespace Common.Messaging;

/// <summary>
/// Gera routing keys para o exchange topic do RabbitMQ.
/// Padrão: {categoria}.{zona}.{detalhe}
/// Instanciado e injetado via construtor (sem métodos static).
/// </summary>
public class RoutingKeys
{
    /// <summary>Routing key para uma medição: medicao.ZONA_ESCOLAR.PM2.5</summary>
    public string Medicao(string zona, string tipoDado) =>
        $"medicao.{Normalizar(zona)}.{Normalizar(tipoDado)}";

    /// <summary>Routing key para heartbeat: heartbeat.ZONA_ESCOLAR.S102</summary>
    public string Heartbeat(string zona, string sensorId) =>
        $"heartbeat.{Normalizar(zona)}.{Normalizar(sensorId)}";

    /// <summary>Routing key para registo inicial: registo.ZONA_ESCOLAR.S102</summary>
    public string Registo(string zona, string sensorId) =>
        $"registo.{Normalizar(zona)}.{Normalizar(sensorId)}";

    /// <summary>Padrão de subscrição do gateway para medições de uma zona (# cobre tipos com ponto, ex. PM2.5).</summary>
    public string MedicaoZona(string zona) => $"medicao.{Normalizar(zona)}.#";

    /// <summary>Padrão de subscrição do gateway para heartbeats de uma zona.</summary>
    public string HeartbeatZona(string zona) => $"heartbeat.{Normalizar(zona)}.*";

    /// <summary>Padrão de subscrição do gateway para registos de uma zona.</summary>
    public string RegistoZona(string zona) => $"registo.{Normalizar(zona)}.*";

    private string Normalizar(string valor) =>
        valor.Replace(' ', '_').ToUpperInvariant();
}
