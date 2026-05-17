namespace Common.Messaging;

/// <summary>
/// Gera routing keys para o exchange topic do RabbitMQ.
/// Padrão: {categoria}.{zona}.{detalhe}
/// </summary>
public static class RoutingKeys
{
    /// <summary>Routing key para uma medição: medicao.ZONA_ESCOLAR.PM2.5</summary>
    public static string Medicao(string zona, string tipoDado) =>
        $"medicao.{Normalizar(zona)}.{Normalizar(tipoDado)}";

    /// <summary>Routing key para heartbeat: heartbeat.ZONA_ESCOLAR.S102</summary>
    public static string Heartbeat(string zona, string sensorId) =>
        $"heartbeat.{Normalizar(zona)}.{Normalizar(sensorId)}";

    /// <summary>Routing key para registo inicial: registo.ZONA_ESCOLAR.S102</summary>
    public static string Registo(string zona, string sensorId) =>
        $"registo.{Normalizar(zona)}.{Normalizar(sensorId)}";

    /// <summary>Padrão de subscrição do gateway para medições de uma zona.</summary>
    public static string MedicaoZona(string zona) => $"medicao.{Normalizar(zona)}.*";

    /// <summary>Padrão de subscrição do gateway para heartbeats de uma zona.</summary>
    public static string HeartbeatZona(string zona) => $"heartbeat.{Normalizar(zona)}.*";

    /// <summary>Padrão de subscrição do gateway para registos de uma zona.</summary>
    public static string RegistoZona(string zona) => $"registo.{Normalizar(zona)}.*";

    private static string Normalizar(string valor) =>
        valor.Replace(' ', '_').ToUpperInvariant();
}
