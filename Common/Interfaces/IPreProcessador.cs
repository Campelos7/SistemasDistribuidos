using Common.Models;

namespace Common.Interfaces;

/// <summary>
/// Contrato do serviço de pré-processamento (implementado via gRPC no servidor dedicado).
/// </summary>
public interface IPreProcessador
{
    /// <summary>
    /// Uniformiza e valida uma medição (escalas, formatos).
    /// </summary>
    Task<Medicao> ProcessarAsync(Medicao medicao, CancellationToken cancellationToken = default);
}
