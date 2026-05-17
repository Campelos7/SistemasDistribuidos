using Common.Grpc.Analise;
using Common.Models;
using Common.Models.Enums;
using Grpc.Core;
using ServicoAnalise.Analyzers;

namespace ServicoAnalise.Services;

/// <summary>
/// Implementação gRPC do serviço de análise e previsão de riscos.
/// </summary>
public class AnaliseGrpcService : AnaliseService.AnaliseServiceBase
{
    private readonly EstatisticasAnalyzer _estatisticas = new();
    private readonly PoluicaoDetector _poluicao = new();
    private readonly RiscoPredictor _risco = new();

    /// <summary>
    /// Executa remotamente o tipo de análise pedido pelo servidor.
    /// </summary>
    public override Task<AnaliseResponse> ExecutarAnalise(AnaliseRequest request, ServerCallContext context)
    {
        try
        {
            var medicoes = request.Medicoes.Select(m => new Medicao(
                m.SensorId,
                m.Zona,
                m.TipoDado,
                m.Valor,
                DateTime.TryParse(m.Timestamp, out var ts) ? ts : DateTime.Now)).ToList();

            string resultado = request.TipoAnalise.ToUpperInvariant() switch
            {
                "ESTATISTICAS" => _estatisticas.Analisar(medicoes),
                "POLUICAO" => _poluicao.Analisar(medicoes),
                "RISCO" => _risco.Analisar(medicoes),
                _ => throw new ArgumentException($"Tipo de análise desconhecido: {request.TipoAnalise}")
            };

            return Task.FromResult(new AnaliseResponse
            {
                Sucesso = true,
                TipoAnalise = request.TipoAnalise,
                ResultadoJson = resultado
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AnaliseResponse
            {
                Sucesso = false,
                TipoAnalise = request.TipoAnalise,
                MensagemErro = ex.Message
            });
        }
    }
}
