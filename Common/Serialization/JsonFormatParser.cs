using System.Text.Json;
using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;

namespace Common.Serialization;

/// <summary>
/// Parser Strategy para medições em formato JSON.
/// Exemplo: {"sensorId":"S102","tipoDado":"PM2.5","valor":78,"timestamp":"2026-03-10T09:15:00"}
/// </summary>
public class JsonFormatParser : IFormatParser
{
    public FormatoDados FormatoSuportado => FormatoDados.Json;

    /// <inheritdoc />
    public Medicao Parse(string payload, string sensorIdPadrao, string zonaPadrao)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        string sensorId = root.TryGetProperty("sensorId", out var s) ? s.GetString() ?? sensorIdPadrao : sensorIdPadrao;
        string zona = root.TryGetProperty("zona", out var z) ? z.GetString() ?? zonaPadrao : zonaPadrao;
        string tipo = root.TryGetProperty("tipoDado", out var t) ? t.GetString() ?? "DESCONHECIDO" : "DESCONHECIDO";
        double valor = root.TryGetProperty("valor", out var v) ? v.GetDouble() : 0;
        DateTime ts = root.TryGetProperty("timestamp", out var tsEl) && DateTime.TryParse(tsEl.GetString(), out var parsed)
            ? parsed
            : DateTime.Now;

        return new Medicao(sensorId, zona, tipo, valor, ts, FormatoDados.Json, payload);
    }
}
