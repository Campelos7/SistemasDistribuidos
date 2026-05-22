using System.Net.Sockets;
using System.Text;

namespace InterfaceVisualizacao;

/// <summary>
/// Cliente TCP que envia pedidos de análise ao Servidor.
/// O Servidor é que invoca o serviço de análise via gRPC (conforme enunciado do TP2).
/// </summary>
public class ServidorTcpClient
{
    private readonly string _host;
    private readonly int _porta;

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
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _porta);

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        string msg = string.Join("|",
            "ANALISE",
            tipoAnalise,
            sensorId ?? "",
            tipoDado ?? "",
            zona ?? "",
            desde?.ToString("yyyy-MM-dd") ?? "",
            ate?.ToString("yyyy-MM-dd") ?? "");

        await writer.WriteLineAsync(msg);

        string? resposta = await reader.ReadLineAsync();
        if (resposta == null)
            throw new InvalidOperationException("Servidor não respondeu ao pedido de análise.");

        if (resposta.StartsWith("ANALISE_OK|"))
            return resposta.Substring("ANALISE_OK|".Length);

        if (resposta.StartsWith("ANALISE_ERROR|"))
            throw new InvalidOperationException(resposta.Substring("ANALISE_ERROR|".Length));

        throw new InvalidOperationException($"Resposta inesperada do servidor: {resposta}");
    }
}
