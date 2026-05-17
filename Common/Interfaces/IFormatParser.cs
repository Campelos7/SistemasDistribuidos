using Common.Models;
using Common.Models.Enums;

namespace Common.Interfaces;

/// <summary>
/// Contrato Strategy para parsing de diferentes formatos de dados (JSON, XML, CSV).
/// </summary>
public interface IFormatParser
{
    FormatoDados FormatoSuportado { get; }

    /// <summary>
    /// Extrai os campos de uma medição a partir de um payload textual.
    /// </summary>
    Medicao Parse(string payload, string sensorIdPadrao, string zonaPadrao);
}
