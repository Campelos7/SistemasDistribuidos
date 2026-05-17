namespace Sensor;

/// <summary>
/// Configuração de um sensor urbano (identificação, zona, tipos suportados).
/// </summary>
public class SensorConfig
{
    public string SensorId { get; }
    public string Zona { get; }
    public IReadOnlyList<string> TiposSuportados { get; }

    public SensorConfig(string sensorId, string zona, IEnumerable<string> tiposSuportados)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
            throw new ArgumentException("SensorId obrigatório.");
        SensorId = sensorId;
        Zona = zona;
        TiposSuportados = tiposSuportados.ToList().AsReadOnly();
    }

    /// <summary>
    /// Cria configuração a partir de argumentos da linha de comandos ou valores por defeito.
    /// Uso: Sensor.exe [sensorId] [zona] [tipos separados por vírgula]
    /// </summary>
    public static SensorConfig FromArgs(string[] args)
    {
        string id = args.Length > 0 ? args[0] : "S102";
        string zona = args.Length > 1 ? args[1] : "ZONA_ESCOLAR";
        string tipos = args.Length > 2 ? args[2] : "PM2.5,TEMP,RUIDO";
        return new SensorConfig(id, zona, tipos.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }
}
