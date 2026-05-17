namespace Common.Config;

/// <summary>
/// Configurações partilhadas entre componentes (endereços, portas, ficheiros).
/// Valores podem ser sobrepostos por variáveis de ambiente.
/// </summary>
public static class AppSettings
{
  public const string ExchangeMonitorizacao = "monitorizacao.urbana";

  public static string RabbitHost => Environment.GetEnvironmentVariable("RABBIT_HOST") ?? "localhost";
  public static string RabbitUser => Environment.GetEnvironmentVariable("RABBIT_USER") ?? "guest";
  public static string RabbitPass => Environment.GetEnvironmentVariable("RABBIT_PASS") ?? "guest";

  public static string PreProcessamentoUrl =>
      Environment.GetEnvironmentVariable("PREPROC_GRPC_URL") ?? "http://localhost:7001";

  public static string AnaliseUrl =>
      Environment.GetEnvironmentVariable("ANALISE_GRPC_URL") ?? "http://localhost:7002";

  public static string ServidorHost =>
      Environment.GetEnvironmentVariable("SERVIDOR_HOST") ?? "127.0.0.1";

  public static int ServidorPorta =>
      int.TryParse(Environment.GetEnvironmentVariable("SERVIDOR_PORT"), out int p) ? p : 6000;

  /// <summary>
  /// Caminho da base SQLite partilhada (Servidor + Interface).
  /// Procura na raiz do repositório; senão usa medicoes.db na pasta atual.
  /// </summary>
  public static string DbPath
  {
    get
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
}
