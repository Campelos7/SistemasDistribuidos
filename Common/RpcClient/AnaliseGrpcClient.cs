using Common.Config;
using Common.Grpc.Analise;
using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;
using Grpc.Net.Client;

namespace Common.RpcClient;

/// <summary>
/// Cliente gRPC que invoca o serviço remoto de análise de dados.
/// </summary>
public class AnaliseGrpcClient : IAnalisador, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AnaliseService.AnaliseServiceClient _client;

    public AnaliseGrpcClient(string? url = null)
    {
        _channel = GrpcChannel.ForAddress(url ?? AppSettings.AnaliseUrl);
        _client = new AnaliseService.AnaliseServiceClient(_channel);
    }

    /// <inheritdoc />
    public async Task<string> ExecutarAsync(
        TipoAnalise tipo,
        IEnumerable<Medicao> medicoes,
        string? sensorId = null,
        string? tipoDado = null,
        string? zona = null,
        DateTime? desde = null,
        DateTime? ate = null,
        CancellationToken cancellationToken = default)
    {
        var request = new AnaliseRequest
        {
            TipoAnalise = tipo.ToString().ToUpperInvariant(),
            SensorId = sensorId ?? string.Empty,
            TipoDado = tipoDado ?? string.Empty,
            Zona = zona ?? string.Empty,
            DataInicio = desde?.ToString("yyyy-MM-ddTHH:mm:ss") ?? string.Empty,
            DataFim = ate?.ToString("yyyy-MM-ddTHH:mm:ss") ?? string.Empty
        };

        request.Medicoes.AddRange(medicoes.Select(m => new MedicaoDado
        {
            SensorId = m.SensorId,
            Zona = m.Zona,
            TipoDado = m.TipoDado,
            Valor = m.Valor,
            Timestamp = m.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss")
        }));

        var response = await _client.ExecutarAnaliseAsync(request, cancellationToken: cancellationToken);

        if (!response.Sucesso)
            throw new InvalidOperationException($"Análise falhou: {response.MensagemErro}");

        return response.ResultadoJson;
    }

    public void Dispose() => _channel.Dispose();
}
