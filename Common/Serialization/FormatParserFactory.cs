using Common.Interfaces;
using Common.Models.Enums;

namespace Common.Serialization;

/// <summary>
/// Factory que seleciona o parser adequado consoante o formato indicado (Strategy + Factory).
/// </summary>
public class FormatParserFactory
{
    private readonly Dictionary<FormatoDados, IFormatParser> _parsers;

    public FormatParserFactory()
    {
        _parsers = new Dictionary<FormatoDados, IFormatParser>
        {
            [FormatoDados.Json] = new JsonFormatParser(),
            [FormatoDados.Xml] = new XmlFormatParser(),
            [FormatoDados.Csv] = new CsvFormatParser()
        };
    }

    /// <summary>
    /// Obtém o parser para o formato indicado.
    /// </summary>
    public IFormatParser Obter(FormatoDados formato)
    {
        if (!_parsers.TryGetValue(formato, out var parser))
            throw new NotSupportedException($"Formato não suportado: {formato}");
        return parser;
    }

    /// <summary>
    /// Converte string (JSON, XML, CSV) para enum FormatoDados.
    /// </summary>
    public static FormatoDados ParseFormato(string? formato)
    {
        return formato?.ToUpperInvariant() switch
        {
            "JSON" => FormatoDados.Json,
            "XML" => FormatoDados.Xml,
            "CSV" => FormatoDados.Csv,
            _ => FormatoDados.None
        };
    }
}
