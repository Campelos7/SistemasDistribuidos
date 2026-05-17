using Common.Grpc.Analise;
using ServicoAnalise.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(7002, listenOptions => listenOptions.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<AnaliseGrpcService>();
app.MapGet("/", () => "Serviço de Análise gRPC ativo na porta 7002.");

Console.WriteLine("[ANALISE] Serviço gRPC a escutar em http://localhost:7002");
app.Run();
