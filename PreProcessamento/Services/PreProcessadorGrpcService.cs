using Common.Grpc.PreProcessamento;
using Common.Models;
using Common.Models.Enums;
using Common.Serialization;
using Grpc.Core;

namespace PreProcessamento.Services;

/// <summary>
/// Implementação gRPC do serviço de pré-processamento.
/// Uniformiza formatos (JSON/XML/CSV) e converte escalas.
/// Dependências injetadas via construtor (DI do ASP.NET).
/// </summary>
public class PreProcessadorGrpcService : PreProcessamentoService.PreProcessamentoServiceBase
{
    private readonly EscalaConverter _escalaConverter;
    private readonly FormatParserFactory _parserFactory;

    public PreProcessadorGrpcService(EscalaConverter escalaConverter, FormatParserFactory parserFactory)
    {
        _escalaConverter = escalaConverter;
        _parserFactory = parserFactory;
    }

    /// <summary>
    /// Processa remotamente uma medição recebida do gateway.
    /// </summary>
    public override Task<MedicaoResponse> ProcessarMedicao(MedicaoRequest request, ServerCallContext context)
    {
        try
        {
            Medicao medicao = ConstruirMedicao(request);
            medicao = AplicarConversoes(medicao);

            return Task.FromResult(new MedicaoResponse
            {
                Sucesso = true,
                SensorId = medicao.SensorId,
                Zona = medicao.Zona,
                TipoDado = medicao.TipoDado,
                Valor = medicao.Valor,
                Timestamp = medicao.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss")
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new MedicaoResponse
            {
                Sucesso = false,
                MensagemErro = ex.Message
            });
        }
    }

    private Medicao ConstruirMedicao(MedicaoRequest request)
    {
        var formato = _parserFactory.ParseFormato(request.Formato);

        if (formato != FormatoDados.None && !string.IsNullOrWhiteSpace(request.Payload))
        {
            var parser = _parserFactory.Obter(formato);
            return parser.Parse(request.Payload, request.SensorId, request.Zona);
        }

        DateTime ts = DateTime.TryParse(request.Timestamp, out var parsed) ? parsed : DateTime.Now;
        return new Medicao(request.SensorId, request.Zona, request.TipoDado, request.Valor, ts, formato);
    }

    private Medicao AplicarConversoes(Medicao medicao)
    {
        double valor = _escalaConverter.NormalizarTemperatura(medicao.Valor, medicao.TipoDado);
        valor = _escalaConverter.NormalizarHumidade(valor, medicao.TipoDado);
        medicao.AtualizarValor(Math.Round(valor, 2));
        return medicao;
    }
}
