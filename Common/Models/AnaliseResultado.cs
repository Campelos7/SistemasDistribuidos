using Common.Models.Enums;

namespace Common.Models;

/// <summary>
/// Resultado persistido de uma análise RPC executada pelo serviço de análise.
/// </summary>
public class AnaliseResultado
{
    public int Id { get; set; }
    public TipoAnalise TipoAnalise { get; }
    public string ParametrosJson { get; }
    public string ResultadoJson { get; }
    public DateTime ExecutadaEm { get; }

    public AnaliseResultado(
        TipoAnalise tipoAnalise,
        string parametrosJson,
        string resultadoJson,
        DateTime? executadaEm = null)
    {
        TipoAnalise = tipoAnalise;
        ParametrosJson = parametrosJson;
        ResultadoJson = resultadoJson;
        ExecutadaEm = executadaEm ?? DateTime.Now;
    }
}
