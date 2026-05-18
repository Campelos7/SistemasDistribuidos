using System.IO;
using System.Net.Sockets;
using System.Text;
using Common.Models;

namespace Gateway.ServerConnection;

/// <summary>
/// Encaminha medições pré-processadas para o servidor principal via TCP (protocolo do TP1).
/// Suporta reconexão automática em caso de perda de ligação.
/// </summary>
public class ServerForwarder : IDisposable
{
    private readonly string _host;
    private readonly int _porta;
    private TcpClient? _cliente;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public ServerForwarder(string host, int porta)
    {
        _host = host;
        _porta = porta;
        Ligar();
    }

    /// <summary>
    /// Estabelece ligação TCP ao servidor.
    /// </summary>
    private void Ligar()
    {
        _cliente?.Close();
        _cliente = new TcpClient(_host, _porta);
        var stream = _cliente.GetStream();
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        _reader = new StreamReader(stream, Encoding.UTF8);
        Console.WriteLine($"[GATEWAY] Ligado ao servidor {_host}:{_porta}");
    }

    /// <summary>
    /// Envia uma medição ao servidor e aguarda ACK. Reconecta automaticamente se necessário.
    /// </summary>
    public async Task<bool> EnviarMedicaoAsync(Medicao medicao, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await _writer!.WriteLineAsync(medicao.ParaMensagemTcp().AsMemory(), cancellationToken);
                string? resposta = await _reader!.ReadLineAsync(cancellationToken);
                Console.WriteLine($"[GATEWAY] Servidor respondeu: {resposta}");
                return resposta == "ACK";
            }
            catch (IOException)
            {
                Console.WriteLine("[GATEWAY] Conexão perdida com o servidor. A reconectar...");
                Ligar();
                await _writer!.WriteLineAsync(medicao.ParaMensagemTcp().AsMemory(), cancellationToken);
                string? resposta = await _reader!.ReadLineAsync(cancellationToken);
                Console.WriteLine($"[GATEWAY] Servidor respondeu (após reconexão): {resposta}");
                return resposta == "ACK";
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _cliente?.Close();
    }
}
