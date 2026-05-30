using System.Net;
using System.Net.Sockets;
using System.Text;
using Common.Models;
using Common.Services;

namespace Servidor.Networking;

/// <summary>
/// Escuta ligações TCP dos gateways e da interface, delegando o processamento ao serviço do servidor.
/// Suporta comandos DATA (medições) e ANALISE (pedidos de análise RPC).
/// </summary>
public class GatewayTcpListener
{
    private readonly ServidorService _servidorService;
    private readonly int _porta;

    public GatewayTcpListener(ServidorService servidorService, int porta)
    {
        _servidorService = servidorService;
        _porta = porta;
    }

    /// <summary>
    /// Aceita clientes em loop, criando uma thread por ligação (concorrência do TP1).
    /// </summary>
    public void Iniciar()
    {
        var listener = new TcpListener(IPAddress.Any, _porta);
        listener.Start();
        Console.WriteLine($"[SERVIDOR] A escutar gateways na porta {_porta}...");

        while (true)
        {
            TcpClient cliente = listener.AcceptTcpClient();
            Thread t = new(() => TratarCliente(cliente));
            t.IsBackground = true;
            t.Start();
        }
    }

    private void TratarCliente(TcpClient cliente)
    {
        Console.WriteLine("[SERVIDOR] Cliente conectado.");
        try
        {
            using var stream = cliente.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            string? linha;
            while ((linha = reader.ReadLine()) != null)
            {
                Console.WriteLine($"[SERVIDOR] Recebido: {linha}");
                string resposta = _servidorService.ProcessarMensagemTcpAsync(linha).GetAwaiter().GetResult();
                writer.WriteLine(resposta);
            }
        }
        catch (IOException)
        {
            // Ligação terminada abruptamente pelo cliente (gateway/interface):
            // encerra apenas esta thread, sem derrubar o servidor.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro na ligação ao cliente: {ex.Message}");
        }
        finally
        {
            cliente.Close();
        }

        Console.WriteLine("[SERVIDOR] Cliente desconectado.");
    }
}

