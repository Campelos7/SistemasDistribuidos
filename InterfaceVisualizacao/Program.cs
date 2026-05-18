using Common.Config;
using Common.DataAccess;
using Common.RpcClient;
using Common.Services;
using InterfaceVisualizacao;

// Interface CLI — consulta BD e pede análises via gRPC (não referencia o projeto Servidor)
var settings = new AppSettings();
var repository = new SqliteMedicaoRepository(settings.DbPath);
using var analiseClient = new AnaliseGrpcClient(settings.AnaliseUrl);
var servidorService = new ServidorService(repository, analiseClient);

var cli = new CliApp(servidorService, settings.DbPath);
await cli.ExecutarAsync();
