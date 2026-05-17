using Common.Config;
using Common.DataAccess;
using Common.RpcClient;
using Common.Services;
using InterfaceVisualizacao;

// Interface CLI — consulta BD e pede análises via gRPC (não referencia o projeto Servidor)
var repository = new SqliteMedicaoRepository(AppSettings.DbPath);
using var analiseClient = new AnaliseGrpcClient();
var servidorService = new ServidorService(repository, analiseClient);

var cli = new CliApp(servidorService, AppSettings.DbPath);
await cli.ExecutarAsync();
