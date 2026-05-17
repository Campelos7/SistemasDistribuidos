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
    /// Lê zona dos argumentos: Gateway.exe [ZONA_ESCOLAR]
    /// </summary>
    public static GatewayConfig FromArgs(string[] args)
    {
        string zona = args.Length > 0 ? args[0] : "ZONA_ESCOLAR";
        return new GatewayConfig(zona);
    }
}
