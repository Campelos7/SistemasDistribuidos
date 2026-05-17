using System.Xml.Linq;
using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;

namespace Common.Serialization;

/// <summary>
/// Parser Strategy para medições em formato XML.
/// </summary>
public class XmlFormatParser : IFormatParser
{
    public FormatoDados FormatoSuportado => FormatoDados.Xml;

    /// <inheritdoc />
    public Medicao Parse(string payload, string sensorIdPadrao, string zonaPadrao)
    {
        var doc = XDocument.Parse(payload);
        var root = doc.Root ?? throw new InvalidOperationException("XML sem elemento raiz.");

        string sensorId = root.Element("sensorId")?.Value ?? sensorIdPadrao;
        string zona = root.Element("zona")?.Value ?? zonaPadrao;
        string tipo = root.Element("tipoDado")?.Value ?? "DESCONHECIDO";
        double valor = double.TryParse(root.Element("valor")?.Value, out var v) ? v : 0;
        DateTime ts = DateTime.TryParse(root.Element("timestamp")?.Value, out var parsed) ? parsed : DateTime.Now;

        return new Medicao(sensorId, zona, tipo, valor, ts, FormatoDados.Xml, payload);
    }
}
