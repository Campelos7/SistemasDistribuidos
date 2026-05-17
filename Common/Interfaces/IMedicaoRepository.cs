using Common.Models;
using Common.Models.Enums;

namespace Common.Interfaces;

/// <summary>
/// Contrato Repository para persistência de medições e resultados de análise.
/// </summary>
public interface IMedicaoRepository
{
    void Guardar(Medicao medicao);

    IEnumerable<Medicao> ObterTodas(
        string? sensorId = null,
        string? tipoDado = null,
        string? zona = null,
        DateTime? desde = null,
        DateTime? ate = null);

    void GuardarAnalise(AnaliseResultado resultado);

    IEnumerable<AnaliseResultado> ObterAnalises(TipoAnalise? tipo = null, int limite = 50);
}
