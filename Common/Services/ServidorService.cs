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
    private readonly IAnalisador? _analisador;

    /// <summary>
    /// Construtor completo — usado pelo Servidor TCP que invoca análises via gRPC.
    /// </summary>
    public ServidorService(IMedicaoRepository repository, IAnalisador analisador)
    {
        _repository = repository;
        _analisador = analisador;
    }

    /// <summary>
    /// Construtor só-leitura — usado pela Interface que apenas consulta a BD.
    /// </summary>
    public ServidorService(IMedicaoRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Processa mensagem TCP (DATA ou ANALISE). Versão assíncrona para suportar RPC.
    /// </summary>
    public async Task<string> ProcessarMensagemTcpAsync(string linha)
    {
        string[] partes = linha.Split('|');

        if (partes.Length == 6 && partes[0] == "DATA")
            return ProcessarData(partes);

        if (partes.Length == 7 && partes[0] == "ANALISE")
            return await ProcessarAnaliseAsync(partes);

        if (partes.Length == 6 && partes[0] == "CONSULTA")
            return ProcessarConsulta(partes);

        if (partes.Length == 3 && partes[0] == "ANALISES")
            return ProcessarConsultaAnalises(partes);

        return "ERROR";
    }

    /// <summary>
    /// Processa mensagem TCP no formato DATA|sensor|zona|tipo|valor|timestamp.
    /// </summary>
    public string ProcessarMensagemTcp(string linha)
    {
        string[] partes = linha.Split('|');
        if (partes.Length == 6 && partes[0] == "DATA")
            return ProcessarData(partes);

        return "ERROR";
    }

    private string ProcessarData(string[] partes)
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

    /// <summary>
    /// Processa pedido TCP ANALISE|tipo|sensor|tipoDado|zona|desde|ate.
    /// O Servidor invoca o serviço de análise via gRPC (conforme enunciado).
    /// </summary>
    private async Task<string> ProcessarAnaliseAsync(string[] partes)
    {
        try
        {
            if (_analisador == null)
                return "ANALISE_ERROR|Serviço de análise não configurado neste processo.";

            TipoAnalise tipo = Enum.Parse<TipoAnalise>(partes[1], ignoreCase: true);
            string? sensorId = string.IsNullOrWhiteSpace(partes[2]) ? null : partes[2];
            string? tipoDado = string.IsNullOrWhiteSpace(partes[3]) ? null : partes[3];
            string? zona = string.IsNullOrWhiteSpace(partes[4]) ? null : partes[4];
            DateTime? desde = DateTime.TryParse(partes[5], out var d1) ? d1 : null;
            DateTime? ate = DateTime.TryParse(partes[6], out var d2) ? d2 : null;

            var resultado = await ExecutarAnaliseAsync(tipo, sensorId, tipoDado, zona, desde, ate);
            Console.WriteLine($"[SERVIDOR] Análise {tipo} executada via gRPC e guardada na BD.");
            return $"ANALISE_OK|{resultado.ResultadoJson}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro na análise: {ex.Message}");
            return $"ANALISE_ERROR|{ex.Message}";
        }
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
        if (_analisador == null)
            throw new InvalidOperationException("Analisador não configurado neste processo.");

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

    /// <summary>
    /// Processa pedido TCP CONSULTA|sensor|tipo|zona|desde|ate.
    /// Devolve as medições serializadas em JSON numa única linha (CONSULTA_OK|json),
    /// para que a Interface obtenha os dados pela rede em vez de aceder à BD.
    /// </summary>
    private string ProcessarConsulta(string[] partes)
    {
        try
        {
            string? sensorId = Vazio(partes[1]);
            string? tipoDado = Vazio(partes[2]);
            string? zona = Vazio(partes[3]);
            DateTime? desde = DateTime.TryParse(partes[4], out var d1) ? d1 : null;
            DateTime? ate = DateTime.TryParse(partes[5], out var d2) ? d2 : null;

            var medicoes = _repository.ObterTodas(sensorId, tipoDado, zona, desde, ate)
                .Take(50)
                .Select(m => new MedicaoDto(
                    m.SensorId,
                    m.Zona,
                    m.TipoDado,
                    m.Valor,
                    m.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss")))
                .ToList();

            return "CONSULTA_OK|" + JsonSerializer.Serialize(medicoes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro na consulta: {ex.Message}");
            return "CONSULTA_ERROR|" + ex.Message;
        }
    }

    /// <summary>
    /// Processa pedido TCP ANALISES|tipo|limite.
    /// Devolve os resultados de análises guardados em JSON (ANALISES_OK|json).
    /// </summary>
    private string ProcessarConsultaAnalises(string[] partes)
    {
        try
        {
            TipoAnalise? tipo = string.IsNullOrWhiteSpace(partes[1])
                ? null
                : Enum.Parse<TipoAnalise>(partes[1], ignoreCase: true);
            int limite = int.TryParse(partes[2], out var l) ? l : 20;

            var analises = _repository.ObterAnalises(tipo, limite)
                .Select(a => new AnaliseDto(
                    a.TipoAnalise.ToString(),
                    a.ExecutadaEm.ToString("yyyy-MM-ddTHH:mm:ss"),
                    a.ParametrosJson,
                    a.ResultadoJson))
                .ToList();

            return "ANALISES_OK|" + JsonSerializer.Serialize(analises);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro na consulta de análises: {ex.Message}");
            return "ANALISES_ERROR|" + ex.Message;
        }
    }

    private static string? Vazio(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

