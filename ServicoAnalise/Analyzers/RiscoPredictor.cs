using System.Text.Json;
using Common.Models;

namespace ServicoAnalise.Analyzers;

/// <summary>
/// Estima risco para saúde pública com base em combinações de fatores ambientais.
/// </summary>
public class RiscoPredictor
{
    /// <summary>
    /// Calcula um índice de risco simplificado (0-100) para a população exposta.
    /// </summary>
    public string Analisar(IEnumerable<Medicao> medicoes)
    {
        var lista = medicoes.ToList();
        if (lista.Count == 0)
            return JsonSerializer.Serialize(new { risco = 0, classificacao = "SEM_DADOS" });

        double score = 0;
        int fatores = 0;

        var pm25 = lista.Where(m => m.TipoDado.Equals("PM2.5", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pm25.Any())
        {
            score += Math.Min(40, pm25.Average(v => v.Valor) / 2);
            fatores++;
        }

        var ruido = lista.Where(m => m.TipoDado.Equals("RUIDO", StringComparison.OrdinalIgnoreCase)).ToList();
        if (ruido.Any())
        {
            score += Math.Min(30, (ruido.Average(v => v.Valor) - 50) / 2);
            fatores++;
        }

        var temp = lista.Where(m => m.TipoDado.Equals("TEMP", StringComparison.OrdinalIgnoreCase)).ToList();
        if (temp.Any())
        {
            double t = temp.Average(v => v.Valor);
            if (t > 35 || t < 0) score += 20;
            fatores++;
        }

        score = Math.Min(100, Math.Max(0, score));
        string classificacao = score switch
        {
            < 25 => "BAIXO",
            < 50 => "MODERADO",
            < 75 => "ALTO",
            _ => "CRITICO"
        };

        return JsonSerializer.Serialize(new
        {
            indiceRisco = Math.Round(score, 1),
            classificacao,
            fatoresConsiderados = fatores,
            recomendacao = classificacao switch
            {
                "CRITICO" => "Evitar exposição prolongada ao ar livre nesta zona.",
                "ALTO" => "Grupos sensíveis devem limitar atividade exterior.",
                _ => "Condições dentro dos parâmetros aceitáveis com monitorização."
            }
        });
    }
}
