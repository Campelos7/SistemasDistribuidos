using Common.Grpc.PreProcessamento;
using PreProcessamento.Services;

// Serviço RPC de pré-processamento — porta HTTP/2 para gRPC
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(7001, listenOptions => listenOptions.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<PreProcessadorGrpcService>();
app.MapGet("/", () => "Serviço de Pré-Processamento gRPC ativo na porta 7001.");

Console.WriteLine("[PRE-PROC] Serviço gRPC a escutar em http://localhost:7001");
app.Run();
