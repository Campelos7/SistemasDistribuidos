using System.Text.Json;
using Common.Models;

namespace ServicoAnalise.Analyzers;

/// <summary>
/// Calcula estatísticas descritivas sobre um conjunto de medições.
/// </summary>
public class EstatisticasAnalyzer
{
    /// <summary>
    /// Devolve média, mínimo, máximo e desvio padrão em JSON.
    /// </summary>
    public string Analisar(IEnumerable<Medicao> medicoes)
    {
        var valores = medicoes.Select(m => m.Valor).ToList();
        if (valores.Count == 0)
            return JsonSerializer.Serialize(new { erro = "Sem medições para analisar." });

        double media = valores.Average();
        double min = valores.Min();
        double max = valores.Max();
        double variancia = valores.Select(v => Math.Pow(v - media, 2)).Average();
        double desvio = Math.Sqrt(variancia);

        return JsonSerializer.Serialize(new
        {
            contagem = valores.Count,
            media = Math.Round(media, 2),
            minimo = min,
            maximo = max,
            desvioPadrao = Math.Round(desvio, 2)
        });
    }
}
