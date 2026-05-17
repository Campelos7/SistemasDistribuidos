using Common.Config;
using Common.Grpc.PreProcessamento;
using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;
using Grpc.Net.Client;

namespace Gateway.RpcClient;

/// <summary>
/// Cliente gRPC que invoca o serviço remoto de pré-processamento.
/// </summary>
public class PreProcessamentoGrpcClient : IPreProcessador, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly PreProcessamentoService.PreProcessamentoServiceClient _client;

    public PreProcessamentoGrpcClient(string? url = null)
    {
        _channel = GrpcChannel.ForAddress(url ?? AppSettings.PreProcessamentoUrl);
        _client = new PreProcessamentoService.PreProcessamentoServiceClient(_channel);
    }

    /// <inheritdoc />
    public async Task<Medicao> ProcessarAsync(Medicao medicao, CancellationToken cancellationToken = default)
    {
        var request = new MedicaoRequest
        {
            SensorId = medicao.SensorId,
            Zona = medicao.Zona,
            TipoDado = medicao.TipoDado,
            Valor = medicao.Valor,
            Timestamp = medicao.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            Formato = medicao.Formato.ToString().ToUpperInvariant(),
            Payload = medicao.PayloadBruto ?? string.Empty
        };

        var response = await _client.ProcessarMedicaoAsync(request, cancellationToken: cancellationToken);

        if (!response.Sucesso)
            throw new InvalidOperationException($"Pré-processamento falhou: {response.MensagemErro}");

        DateTime ts = DateTime.TryParse(response.Timestamp, out var parsed) ? parsed : medicao.Timestamp;
        var resultado = new Medicao(response.SensorId, response.Zona, response.TipoDado, response.Valor, ts);
        return resultado;
    }

    public void Dispose() => _channel.Dispose();
}
