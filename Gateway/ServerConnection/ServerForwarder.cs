using System.Net.Sockets;
using System.Text;
using Common.Config;
using Common.Models;

namespace Gateway.ServerConnection;

/// <summary>
/// Encaminha medições pré-processadas para o servidor principal via TCP (protocolo do TP1).
/// </summary>
public class ServerForwarder : IDisposable
{
    private readonly TcpClient _cliente;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public ServerForwarder()
    {
        _cliente = new TcpClient(AppSettings.ServidorHost, AppSettings.ServidorPorta);
        var stream = _cliente.GetStream();
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        _reader = new StreamReader(stream, Encoding.UTF8);
        Console.WriteLine($"[GATEWAY] Ligado ao servidor {AppSettings.ServidorHost}:{AppSettings.ServidorPorta}");
    }

    /// <summary>
    /// Envia uma medição ao servidor e aguarda ACK.
    /// </summary>
    public async Task<bool> EnviarMedicaoAsync(Medicao medicao, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(medicao.ParaMensagemTcp().AsMemory(), cancellationToken);
            string? resposta = await _reader.ReadLineAsync(cancellationToken);
            Console.WriteLine($"[GATEWAY] Servidor respondeu: {resposta}");
            return resposta == "ACK";
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
        _cliente.Close();
    }
}
