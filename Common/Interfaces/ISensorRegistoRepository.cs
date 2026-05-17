using Common.Models;

namespace Common.Interfaces;

/// <summary>
/// Contrato para leitura e escrita do ficheiro CSV de sensores do gateway.
/// </summary>
public interface ISensorRegistoRepository
{
    IReadOnlyDictionary<string, SensorRegisto> CarregarTodos();
    void GuardarTodos(IEnumerable<SensorRegisto> sensores);
    void AtualizarUltimaSincronizacao(string sensorId, DateTime momento);
    void MarcarDesativado(string sensorId);
}
