using Common.Models.Enums;
using Common.Services;

namespace InterfaceVisualizacao;

/// <summary>
/// Interface de linha de comandos para consultar dados e pedir análises.
/// </summary>
public class CliApp
{
    private readonly ServidorService _servidor;
    private readonly string _dbPath;

    public CliApp(ServidorService servidor, string dbPath)
    {
        _servidor = servidor;
        _dbPath = dbPath;
    }

    /// <summary>
    /// Executa o menu interativo principal.
    /// </summary>
    public async Task ExecutarAsync()
    {
        Console.WriteLine("=== Interface de Visualização — Monitorização Urbana (TP2) ===");
        Console.WriteLine($"Base de dados: {_dbPath}\n");

        while (true)
        {
            MostrarMenu();
            string? opcao = Console.ReadLine();

            switch (opcao?.Trim())
            {
                case "1":
                    ConsultarMedicoes();
                    break;
                case "2":
                    await PedirAnaliseAsync();
                    break;
                case "3":
                    VerAnalises();
                    break;
                case "0":
                    return;
                default:
                    Console.WriteLine("Opção inválida.\n");
                    break;
            }
        }
    }

    private static void MostrarMenu()
    {
        Console.WriteLine("1 - Consultar medições");
        Console.WriteLine("2 - Pedir nova análise (RPC)");
        Console.WriteLine("3 - Ver resultados de análises guardados");
        Console.WriteLine("0 - Sair");
        Console.Write("Escolha: ");
    }

    private void ConsultarMedicoes()
    {
        string? sensor = PedirOpcional("ID do sensor (Enter = todos)");
        string? tipo = PedirOpcional("Tipo de dado (Enter = todos)");
        string? zona = PedirOpcional("Zona (Enter = todas)");
        DateTime? desde = PedirDataOpcional("Data início (yyyy-MM-dd ou Enter)");
        DateTime? ate = PedirDataOpcional("Data fim (yyyy-MM-dd ou Enter)");

        var medicoes = _servidor.ConsultarMedicoes(sensor, tipo, zona, desde, ate).Take(50);

        Console.WriteLine("\n--- Medições ---");
        int n = 0;
        foreach (var m in medicoes)
        {
            Console.WriteLine($"{m.Timestamp:yyyy-MM-dd HH:mm} | {m.SensorId} | {m.Zona} | {m.TipoDado} | {m.Valor}");
            n++;
        }
        Console.WriteLine($"Total mostrado: {n}\n");
    }

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
            var resultado = await _servidor.ExecutarAnaliseAsync(tipo, sensor, tipoDado, zona, desde, ate);
            Console.WriteLine("\n--- Resultado da análise ---");
            Console.WriteLine(resultado.ResultadoJson);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}\n");
        }
    }

    private void VerAnalises()
    {
        var analises = _servidor.ConsultarAnalises(limite: 20);
        Console.WriteLine("\n--- Análises guardadas ---");
        foreach (var a in analises)
        {
            Console.WriteLine($"[{a.ExecutadaEm:yyyy-MM-dd HH:mm}] {a.TipoAnalise}");
            Console.WriteLine($"  Parâmetros: {a.ParametrosJson}");
            Console.WriteLine($"  Resultado:  {a.ResultadoJson}\n");
        }
    }

    private static string? PedirOpcional(string prompt)
    {
        Console.Write($"{prompt}: ");
        string? s = Console.ReadLine();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static DateTime? PedirDataOpcional(string prompt)
    {
        Console.Write($"{prompt}: ");
        string? s = Console.ReadLine();
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }
}
