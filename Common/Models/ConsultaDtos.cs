namespace Common.Models;

/// <summary>
/// DTO de medição transportado na rede (TCP) entre o Servidor e a Interface.
/// Mantém a Interface desacoplada da base de dados: os dados chegam serializados,
/// nunca por acesso direto ao ficheiro SQLite.
/// </summary>
public record MedicaoDto(
    string SensorId,
    string Zona,
    string TipoDado,
    double Valor,
    string Timestamp);

/// <summary>
/// DTO de resultado de análise transportado na rede (TCP) entre o Servidor e a Interface.
/// </summary>
public record AnaliseDto(
    string TipoAnalise,
    string ExecutadaEm,
    string ParametrosJson,
    string ResultadoJson);
