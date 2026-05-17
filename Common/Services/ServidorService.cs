using System.Text.Json;
using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;

namespace Common.Services;

/// <summary>
/// Orquestra persistência de medições e pedidos de análise RPC.
/// Partilhado entre o Servidor TCP e a Interface de visualização.
/// </summary>
public class ServidorService
{
    private readonly IMedicaoRepository _repository;
    private readonly IAnalisador _analisador;

    public ServidorService(IMedicaoRepository repository, IAnalisador analisador)
    {
        _repository = repository;
        _analisador = analisador;
    }

    /// <summary>
    /// Processa mensagem TCP no formato DATA|sensor|zona|tipo|valor|timestamp.
    /// </summary>
    public string ProcessarMensagemTcp(string linha)
    {
        string[] partes = linha.Split('|');
        if (partes.Length == 6 && partes[0] == "DATA")
        {
            try
            {
                DateTime ts = DateTime.Parse(partes[5]);
                double valor = double.Parse(partes[4], System.Globalization.CultureInfo.InvariantCulture);
                var medicao = new Medicao(partes[1], partes[2], partes[3], valor, ts);
                _repository.Guardar(medicao);
                Console.WriteLine($"[SERVIDOR] Medição guardada: {medicao.SensorId} | {medicao.TipoDado} | {medicao.Valor}");
                return "ACK";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVIDOR] Erro: {ex.Message}");
                return "ERROR";
            }
        }

        return "ERROR";
    }

    /// <summary>
    /// Executa análise RPC sobre medições filtradas e persiste o resultado.
    /// </summary>
    public async Task<AnaliseResultado> ExecutarAnaliseAsync(
        TipoAnalise tipo,
        string? sensorId = null,
        string? tipoDado = null,
        string? zona = null,
        DateTime? desde = null,
        DateTime? ate = null)
    {
        var medicoes = _repository.ObterTodas(sensorId, tipoDado, zona, desde, ate).ToList();

        string resultadoJson = await _analisador.ExecutarAsync(
            tipo, medicoes, sensorId, tipoDado, zona, desde, ate);

        var parametros = JsonSerializer.Serialize(new { sensorId, tipoDado, zona, desde, ate, total = medicoes.Count });
        var resultado = new AnaliseResultado(tipo, parametros, resultadoJson);
        _repository.GuardarAnalise(resultado);

        return resultado;
    }

    /// <summary>
    /// Consulta medições com filtros opcionais.
    /// </summary>
    public IEnumerable<Medicao> ConsultarMedicoes(
        string? sensorId = null,
        string? tipoDado = null,
        string? zona = null,
        DateTime? desde = null,
        DateTime? ate = null) =>
        _repository.ObterTodas(sensorId, tipoDado, zona, desde, ate);

    /// <summary>
    /// Consulta resultados de análises guardados.
    /// </summary>
    public IEnumerable<AnaliseResultado> ConsultarAnalises(TipoAnalise? tipo = null, int limite = 20) =>
        _repository.ObterAnalises(tipo, limite);
}
