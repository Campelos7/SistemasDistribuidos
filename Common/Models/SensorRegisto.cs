using Common.Models.Enums;

namespace Common.Models;

/// <summary>
/// Informação de um sensor registado no ficheiro CSV do gateway.
/// </summary>
public class SensorRegisto
{
    public string SensorId { get; }
    public EstadoSensor Estado { get; private set; }
    public string Zona { get; }
    public IReadOnlyList<string> TiposSuportados { get; }
    public DateTime? UltimaSincronizacao { get; private set; }

    public SensorRegisto(
        string sensorId,
        EstadoSensor estado,
        string zona,
        IEnumerable<string> tiposSuportados,
        DateTime? ultimaSincronizacao = null)
    {
        SensorId = sensorId;
        Estado = estado;
        Zona = zona;
        TiposSuportados = tiposSuportados.ToList().AsReadOnly();
        UltimaSincronizacao = ultimaSincronizacao;
    }

    /// <summary>
    /// Indica se o sensor pode enviar medições (estado ativo).
    /// </summary>
    public bool EstaOperacional => Estado == EstadoSensor.Ativo;

    /// <summary>
    /// Verifica se o tipo de dado está na lista de tipos suportados.
    /// </summary>
    public bool SuportaTipo(string tipoDado) =>
        TiposSuportados.Any(t => t.Equals(tipoDado, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Atualiza a data da última comunicação (heartbeat ou medição).
    /// </summary>
    public void AtualizarSincronizacao(DateTime momento) => UltimaSincronizacao = momento;

    /// <summary>
    /// Marca o sensor como desativado (ex.: timeout de heartbeat).
    /// </summary>
    public void MarcarDesativado() => Estado = EstadoSensor.Desativado;
}
