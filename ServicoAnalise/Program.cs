using ServicoAnalise.Analyzers;
using ServicoAnalise.Services;

// Serviço RPC de análise — porta HTTP/2 para gRPC
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(7002, listenOptions => listenOptions.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

// Registar analyzers no contentor DI do ASP.NET
builder.Services.AddSingleton<EstatisticasAnalyzer>();
builder.Services.AddSingleton<PoluicaoDetector>();
builder.Services.AddSingleton<RiscoPredictor>();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<AnaliseGrpcService>();
app.MapGet("/", () => "Serviço de Análise gRPC ativo na porta 7002.");

Console.WriteLine("[ANALISE] Serviço gRPC a escutar em http://localhost:7002");
app.Run();
