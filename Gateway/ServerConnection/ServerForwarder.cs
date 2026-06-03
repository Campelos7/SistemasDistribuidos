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
    }

    /// <summary>
    /// Estabelece ligação TCP ao servidor (lazy — só quando necessário enviar).
    /// </summary>
    private void Ligar()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _cliente?.Close();
        _cliente = new TcpClient(_host, _porta);
        var stream = _cliente.GetStream();
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        _reader = new StreamReader(stream, Encoding.UTF8);
        Console.WriteLine($"[GATEWAY] Ligado ao servidor {_host}:{_porta}");
    }

    private void EnsureLigado()
    {
        if (_cliente is { Connected: true })
            return;
        Ligar();
    }

    /// <summary>
    /// Envia uma medição ao servidor e aguarda ACK. Reconecta automaticamente se necessário.
    /// </summary>
    /// <returns><c>true</c> se o Servidor respondeu <c>ACK</c> (medição enfileirada/persistida).</returns>
    /// <exception cref="IOException">Quando a ligação TCP falha e a reconexão também falha.</exception>
    public async Task<bool> EnviarMedicaoAsync(Medicao medicao, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            try
            {
                EnsureLigado();
                return await EnviarEReceberAckAsync(medicao, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                Console.WriteLine("[GATEWAY] Conexão perdida com o servidor. A reconectar...");
                try
                {
                    Ligar();
                    return await EnviarEReceberAckAsync(medicao, cancellationToken);
                }
                catch (Exception retryEx) when (retryEx is IOException or SocketException)
                {
                    throw new IOException("Falha ao enviar medição ao Servidor após reconexão.", ex);
                }
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<bool> EnviarEReceberAckAsync(Medicao medicao, CancellationToken cancellationToken)
    {
        await _writer!.WriteLineAsync(medicao.ParaMensagemTcp().AsMemory(), cancellationToken);
        string? resposta = await _reader!.ReadLineAsync(cancellationToken);
        Console.WriteLine($"[GATEWAY] Servidor respondeu: {resposta}");
        return resposta == "ACK";
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _cliente?.Close();
    }
}
