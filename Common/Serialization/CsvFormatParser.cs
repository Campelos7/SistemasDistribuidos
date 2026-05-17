using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;

namespace Common.Serialization;

/// <summary>
/// Parser Strategy para medições em formato CSV (uma linha de cabeçalho opcional).
/// Formato: sensorId,zona,tipoDado,valor,timestamp
/// </summary>
public class CsvFormatParser : IFormatParser
{
    public FormatoDados FormatoSuportado => FormatoDados.Csv;

    /// <inheritdoc />
    public Medicao Parse(string payload, string sensorIdPadrao, string zonaPadrao)
    {
        var linhas = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string linhaDados = linhas.Length > 1 && linhas[0].Contains("sensorId", StringComparison.OrdinalIgnoreCase)
            ? linhas[1]
            : linhas[0];

        var campos = linhaDados.Split(',');
        if (campos.Length < 4)
            throw new FormatException("CSV inválido: são necessários pelo menos sensorId, zona, tipoDado e valor.");

        string sensorId = string.IsNullOrWhiteSpace(campos[0]) ? sensorIdPadrao : campos[0].Trim();
        string zona = string.IsNullOrWhiteSpace(campos[1]) ? zonaPadrao : campos[1].Trim();
        string tipo = campos[2].Trim();
        double valor = double.Parse(campos[3].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        DateTime ts = campos.Length > 4 && DateTime.TryParse(campos[4].Trim(), out var parsed) ? parsed : DateTime.Now;

        return new Medicao(sensorId, zona, tipo, valor, ts, FormatoDados.Csv, payload);
    }
}
