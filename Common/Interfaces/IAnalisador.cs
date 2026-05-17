using Common.Models;
using Common.Models.Enums;

namespace Common.Interfaces;

/// <summary>
/// Contrato do serviço de análise de dados ambientais.
/// </summary>
public interface IAnalisador
{
    /// <summary>
    /// Executa uma análise sobre o conjunto de medições fornecido.
    /// </summary>
    Task<string> ExecutarAsync(
        TipoAnalise tipo,
        IEnumerable<Medicao> medicoes,
        string? sensorId = null,
        string? tipoDado = null,
        string? zona = null,
        DateTime? desde = null,
        DateTime? ate = null,
        CancellationToken cancellationToken = default);
}
