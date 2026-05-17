using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;

namespace Common.DataAccess;

/// <summary>
/// Persistência do registo de sensores em ficheiro CSV (formato do TP1).
/// Formato: sensor_id:estado:zona:[tipos]:last_sync
/// </summary>
public class CsvSensorRegistoRepository : ISensorRegistoRepository
{
    private readonly string _caminhoFicheiro;
    private readonly object _lock = new();

    public CsvSensorRegistoRepository(string caminhoFicheiro) => _caminhoFicheiro = caminhoFicheiro;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, SensorRegisto> CarregarTodos()
    {
        lock (_lock)
            return CarregarTodosSemLock();
    }

    /// <inheritdoc />
    public void GuardarTodos(IEnumerable<SensorRegisto> sensores)
    {
        lock (_lock)
        {
            GuardarTodosSemLock(sensores);
        }
    }

    private Dictionary<string, SensorRegisto> CarregarTodosSemLock()
    {
        var sensores = new Dictionary<string, SensorRegisto>();
        if (!File.Exists(_caminhoFicheiro))
            return sensores;

        foreach (string linha in File.ReadAllLines(_caminhoFicheiro))
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;
            var registo = ParseLinha(linha);
            if (registo != null)
                sensores[registo.SensorId] = registo;
        }
        return sensores;
    }

    private void GuardarTodosSemLock(IEnumerable<SensorRegisto> sensores)
    {
            var linhas = sensores.Select(s =>
            {
                string tipos = "[" + string.Join(",", s.TiposSuportados) + "]";
                string sync = s.UltimaSincronizacao?.ToString("yyyy-MM-ddTHH:mm:ss") ?? "";
                string estado = s.Estado switch
                {
                    EstadoSensor.Ativo => "ativo",
                    EstadoSensor.Manutencao => "manutencao",
                    EstadoSensor.Desativado => "desativado",
                    _ => "desativado"
                };
                return $"{s.SensorId}:{estado}:{s.Zona}:{tipos}:{sync}";
            });
            File.WriteAllLines(_caminhoFicheiro, linhas);
    }

    /// <inheritdoc />
    public void AtualizarUltimaSincronizacao(string sensorId, DateTime momento)
    {
        lock (_lock)
        {
            var todos = CarregarTodosSemLock().Values.ToList();
            var sensor = todos.FirstOrDefault(s => s.SensorId == sensorId);
            if (sensor == null) return;
            sensor.AtualizarSincronizacao(momento);
            GuardarTodosSemLock(todos);
        }
    }

    /// <inheritdoc />
    public void MarcarDesativado(string sensorId)
    {
        lock (_lock)
        {
            var todos = CarregarTodosSemLock().Values.ToList();
            var sensor = todos.FirstOrDefault(s => s.SensorId == sensorId);
            if (sensor == null) return;
            sensor.MarcarDesativado();
            GuardarTodosSemLock(todos);
        }
    }

  private static SensorRegisto? ParseLinha(string linha)
    {
        string[] partes = linha.Split(':');
        if (partes.Length < 5) return null;

        string tiposStr = partes[3].Trim('[', ']');
        var tipos = tiposStr.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var estado = partes[1].ToLowerInvariant() switch
        {
            "ativo" => EstadoSensor.Ativo,
            "manutencao" => EstadoSensor.Manutencao,
            _ => EstadoSensor.Desativado
        };

        DateTime? sync = DateTime.TryParse(partes[4], out var dt) ? dt : null;
        return new SensorRegisto(partes[0], estado, partes[2], tipos, sync);
    }
}
