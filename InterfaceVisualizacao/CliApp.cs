using Common.Models.Enums;

namespace InterfaceVisualizacao;

/// <summary>
/// Interface de linha de comandos para consultar dados e pedir análises.
/// TODAS as operações (consultas e análises) passam pelo Servidor via TCP.
/// A Interface não acede à base de dados — é um cliente puro de rede.
/// </summary>
public class CliApp
{
    private readonly ServidorTcpClient _tcpClient;
    private readonly string _servidorEndpoint;

    public CliApp(ServidorTcpClient tcpClient, string servidorEndpoint)
    {
        _tcpClient = tcpClient;
        _servidorEndpoint = servidorEndpoint;
    }

    /// <summary>
    /// Executa o menu interativo principal.
    /// </summary>
    public async Task ExecutarAsync()
    {
        Console.WriteLine("=== Interface de Visualização — Monitorização Urbana (TP2) ===");
        Console.WriteLine($"Servidor (TCP): {_servidorEndpoint}\n");

        while (true)
        {
            MostrarMenu();
            string? opcao = Console.ReadLine();

            switch (opcao?.Trim())
            {
                case "1":
                    await ConsultarMedicoesAsync();
                    break;
                case "2":
                    await PedirAnaliseAsync();
                    break;
                case "3":
                    await VerAnalisesAsync();
                    break;
                case "0":
                    return;
                default:
                    Console.WriteLine("Opção inválida.\n");
                    break;
            }
        }
    }

    private void MostrarMenu()
    {
        Console.WriteLine("1 - Consultar medições");
        Console.WriteLine("2 - Pedir nova análise (via Servidor -> gRPC)");
        Console.WriteLine("3 - Ver resultados de análises guardados");
        Console.WriteLine("0 - Sair");
        Console.Write("Escolha: ");
    }

    private async Task ConsultarMedicoesAsync()
    {
        string? sensor = PedirOpcional("ID do sensor (Enter = todos)");
        string? tipo = PedirOpcional("Tipo de dado (Enter = todos)");
        string? zona = PedirOpcional("Zona (Enter = todas)");
        DateTime? desde = PedirDataOpcional("Data início (yyyy-MM-dd ou Enter)");
        DateTime? ate = PedirDataOpcional("Data fim (yyyy-MM-dd ou Enter)");

        try
        {
            var medicoes = await _tcpClient.ConsultarMedicoesAsync(sensor, tipo, zona, desde, ate);

            Console.WriteLine("\n--- Medições ---");
            foreach (var m in medicoes)
            {
                Console.WriteLine($"{m.Timestamp.Replace('T', ' ')} | {m.SensorId} | {m.Zona} | {m.TipoDado} | {m.Valor}");
            }
            Console.WriteLine($"Total mostrado: {medicoes.Count}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Envia pedido de análise ao Servidor via TCP.
    /// O Servidor invoca o ServicoAnalise via gRPC e devolve o resultado.
    /// </summary>
    private async Task PedirAnaliseAsync()
    {
        Console.WriteLine("Tipos: 1=Estatisticas 2=Poluicao 3=Risco");
        string? t = Console.ReadLine();
        TipoAnalise tipo = t switch
        {
            "2" => TipoAnalise.Poluicao,
            "3" => TipoAnalise.Risco,
            _ => TipoAnalise.Estatisticas
        };

        string? sensor = PedirOpcional("ID do sensor");
        string? tipoDado = PedirOpcional("Tipo de dado");
        string? zona = PedirOpcional("Zona");
        DateTime? desde = PedirDataOpcional("Data início");
        DateTime? ate = PedirDataOpcional("Data fim");

        try
        {
            string resultadoJson = await _tcpClient.PedirAnaliseAsync(
                tipo.ToString().ToUpperInvariant(), sensor, tipoDado, zona, desde, ate);
            Console.WriteLine("\n--- Resultado da análise ---");
            Console.WriteLine(resultadoJson);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}\n");
        }
    }

    private async Task VerAnalisesAsync()
    {
        try
        {
            var analises = await _tcpClient.ConsultarAnalisesAsync(tipo: null, limite: 20);
            Console.WriteLine("\n--- Análises guardadas ---");
            foreach (var a in analises)
            {
                Console.WriteLine($"[{a.ExecutadaEm.Replace('T', ' ')}] {a.TipoAnalise}");
                Console.WriteLine($"  Parâmetros: {a.ParametrosJson}");
                Console.WriteLine($"  Resultado:  {a.ResultadoJson}\n");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}\n");
        }
    }

    private string? PedirOpcional(string prompt)
    {
        Console.Write($"{prompt}: ");
        string? s = Console.ReadLine();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private DateTime? PedirDataOpcional(string prompt)
    {
        Console.Write($"{prompt}: ");
        string? s = Console.ReadLine();
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }
}

