namespace Gateway;

/// <summary>
/// Configuração do gateway: zona gerida, ficheiro CSV e timeouts.
/// </summary>
public class GatewayConfig
{
    public string ZonaGerida { get; }
    public string FicheiroSensores { get; }
    public int TimeoutHeartbeatSegundos { get; }

    public GatewayConfig(string zonaGerida, string ficheiroSensores = "sensores.csv", int timeoutSegundos = 90)
    {
        ZonaGerida = zonaGerida;
        FicheiroSensores = ficheiroSensores;
        TimeoutHeartbeatSegundos = timeoutSegundos;
    }

    /// <summary>
    /// Cria configuração a partir dos argumentos: Gateway.exe [ZONA_ESCOLAR]
    /// </summary>
    public GatewayConfig(string[] args)
        : this(args.Length > 0 ? args[0] : "ZONA_ESCOLAR")
    {
    }
}
