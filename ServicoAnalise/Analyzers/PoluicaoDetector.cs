using System.Text.Json;
using Common.Models;

namespace ServicoAnalise.Analyzers;

/// <summary>
/// Deteta padrões de poluição (PM2.5, PM10, qualidade do ar elevados).
/// </summary>
public class PoluicaoDetector
{
    private const double LimiarPm25 = 55;
    private const double LimiarPm10 = 100;
    private const double LimiarRuido = 70;

    /// <summary>
    /// Identifica medições acima dos limiares de referência OMS simplificados.
    /// </summary>
    public string Analisar(IEnumerable<Medicao> medicoes)
    {
        var alertas = new List<object>();

        foreach (var m in medicoes)
        {
            bool alerta = m.TipoDado.ToUpperInvariant() switch
            {
                "PM2.5" => m.Valor > LimiarPm25,
                "PM10" => m.Valor > LimiarPm10,
                "RUIDO" => m.Valor > LimiarRuido,
                "AR" or "QUALIDADEAR" => m.Valor > 150,
                _ => false
            };

            if (alerta)
            {
                alertas.Add(new
                {
                    m.SensorId,
                    m.Zona,
                    m.TipoDado,
                    m.Valor,
                    timestamp = m.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
                    nivel = ClassificarNivel(m.TipoDado, m.Valor)
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            totalAlertas = alertas.Count,
            alertas
        });
    }

    private string ClassificarNivel(string tipo, double valor)
    {
        if (tipo.Equals("PM2.5", StringComparison.OrdinalIgnoreCase))
            return valor > 75 ? "CRITICO" : "ELEVADO";
        return "ELEVADO";
    }
}
