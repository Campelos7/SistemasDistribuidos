namespace Common.Config;

/// <summary>
/// Configurações partilhadas entre componentes (endereços, portas, ficheiros).
/// Valores podem ser sobrepostos por variáveis de ambiente.
/// Instanciado uma vez e injetado nos componentes via construtor (DIP).
/// </summary>
public class AppSettings
{
    public string ExchangeMonitorizacao { get; }
    public string RabbitHost { get; }
    public string RabbitUser { get; }
    public string RabbitPass { get; }
    public string PreProcessamentoUrl { get; }
    public string AnaliseUrl { get; }
    public string ServidorHost { get; }
    public int ServidorPorta { get; }

    /// <summary>
    /// Caminho da base SQLite partilhada (Servidor + Interface).
    /// </summary>
    public string DbPath { get; }

    public AppSettings()
    {
        ExchangeMonitorizacao = "monitorizacao.urbana";
        RabbitHost = Environment.GetEnvironmentVariable("RABBIT_HOST") ?? "localhost";
        RabbitUser = Environment.GetEnvironmentVariable("RABBIT_USER") ?? "guest";
        RabbitPass = Environment.GetEnvironmentVariable("RABBIT_PASS") ?? "guest";
        PreProcessamentoUrl = Environment.GetEnvironmentVariable("PREPROC_GRPC_URL") ?? "http://localhost:7001";
        AnaliseUrl = Environment.GetEnvironmentVariable("ANALISE_GRPC_URL") ?? "http://localhost:7002";
        ServidorHost = Environment.GetEnvironmentVariable("SERVIDOR_HOST") ?? "127.0.0.1";
        ServidorPorta = int.TryParse(Environment.GetEnvironmentVariable("SERVIDOR_PORT"), out int p) ? p : 6000;
        DbPath = ResolverDbPath();
    }

    /// <summary>
    /// Procura a BD na raiz do repositório; senão usa medicoes.db na pasta atual.
    /// </summary>
    private string ResolverDbPath()
    {
        var env = Environment.GetEnvironmentVariable("DB_PATH");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "TrabalhoPratico.sln")))
                return Path.Combine(dir, "medicoes.db");
            dir = Directory.GetParent(dir)?.FullName;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "medicoes.db");
    }
}
