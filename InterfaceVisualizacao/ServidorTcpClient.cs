using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Common.Models;

namespace InterfaceVisualizacao;

/// <summary>
/// Cliente TCP da Interface. TODA a comunicação com os dados passa por aqui:
/// pedidos de análise E consultas de medições/análises. A Interface nunca
/// acede à base de dados — só o Servidor toca no SQLite.
/// </summary>
public class ServidorTcpClient
{
    private readonly string _host;
    private readonly int _porta;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public ServidorTcpClient(string host, int porta)
    {
        _host = host;
        _porta = porta;
    }

    /// <summary>
    /// Envia pedido ANALISE ao Servidor via TCP e devolve o JSON do resultado.
    /// Protocolo: ANALISE|tipo|sensorId|tipoDado|zona|desde|ate → ANALISE_OK|json ou ANALISE_ERROR|msg
    /// </summary>
    public async Task<string> PedirAnaliseAsync(
        string tipoAnalise,
        string? sensorId,
        string? tipoDado,
        string? zona,
        DateTime? desde,
        DateTime? ate)
    {
        string msg = string.Join("|",
            "ANALISE",
            tipoAnalise,
            sensorId ?? "",
            tipoDado ?? "",
            zona ?? "",
            desde?.ToString("yyyy-MM-dd") ?? "",
            ate?.ToString("yyyy-MM-dd") ?? "");

        return await EnviarReceberAsync(msg, "ANALISE_OK|", "ANALISE_ERROR|");
    }

    /// <summary>
    /// Consulta medições no Servidor via TCP.
    /// Protocolo: CONSULTA|sensor|tipo|zona|desde|ate → CONSULTA_OK|json ou CONSULTA_ERROR|msg
    /// </summary>
    public async Task<List<MedicaoDto>> ConsultarMedicoesAsync(
        string? sensorId,
        string? tipoDado,
        string? zona,
        DateTime? desde,
        DateTime? ate)
    {
        string msg = string.Join("|",
            "CONSULTA",
            sensorId ?? "",
            tipoDado ?? "",
            zona ?? "",
            desde?.ToString("yyyy-MM-dd") ?? "",
            ate?.ToString("yyyy-MM-dd") ?? "");

        string payload = await EnviarReceberAsync(msg, "CONSULTA_OK|", "CONSULTA_ERROR|");
        return JsonSerializer.Deserialize<List<MedicaoDto>>(payload, _jsonOpts) ?? new List<MedicaoDto>();
    }

    /// <summary>
    /// Consulta análises guardadas no Servidor via TCP.
    /// Protocolo: ANALISES|tipo|limite → ANALISES_OK|json ou ANALISES_ERROR|msg
    /// </summary>
    public async Task<List<AnaliseDto>> ConsultarAnalisesAsync(string? tipo, int limite)
    {
        string msg = string.Join("|", "ANALISES", tipo ?? "", limite.ToString());

        string payload = await EnviarReceberAsync(msg, "ANALISES_OK|", "ANALISES_ERROR|");
        return JsonSerializer.Deserialize<List<AnaliseDto>>(payload, _jsonOpts) ?? new List<AnaliseDto>();
    }

    /// <summary>
    /// Abre ligação TCP, envia uma linha e devolve o payload da resposta (após o prefixo OK).
    /// Lança exceção se o Servidor responder com o prefixo de erro ou algo inesperado.
    /// </summary>
    private async Task<string> EnviarReceberAsync(string msg, string okPrefix, string erroPrefix)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _porta);

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        await writer.WriteLineAsync(msg);

        string? resposta = await reader.ReadLineAsync();
        if (resposta == null)
            throw new InvalidOperationException("Servidor não respondeu.");

        if (resposta.StartsWith(okPrefix))
            return resposta.Substring(okPrefix.Length);

        if (resposta.StartsWith(erroPrefix))
            throw new InvalidOperationException(resposta.Substring(erroPrefix.Length));

        throw new InvalidOperationException($"Resposta inesperada do servidor: {resposta}");
    }
}
