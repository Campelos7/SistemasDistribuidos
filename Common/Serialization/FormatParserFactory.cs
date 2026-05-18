using Common.Interfaces;
using Common.Models.Enums;

namespace Common.Serialization;

/// <summary>
/// Factory que seleciona o parser adequado consoante o formato indicado (Strategy + Factory).
/// Recebe os parsers via construtor (DIP).
/// </summary>
public class FormatParserFactory
{
    private readonly Dictionary<FormatoDados, IFormatParser> _parsers;

    public FormatParserFactory(IEnumerable<IFormatParser> parsers)
    {
        _parsers = new Dictionary<FormatoDados, IFormatParser>();
        foreach (var parser in parsers)
            _parsers[parser.FormatoSuportado] = parser;
    }

    /// <summary>
    /// Construtor de conveniência que regista os parsers padrão.
    /// </summary>
    public FormatParserFactory()
        : this(new IFormatParser[]
        {
            new JsonFormatParser(),
            new XmlFormatParser(),
            new CsvFormatParser()
        })
    {
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
    public FormatoDados ParseFormato(string? formato)
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
