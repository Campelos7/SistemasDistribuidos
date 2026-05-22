using Common.Config;
using Common.DataAccess;
using Common.Services;
using InterfaceVisualizacao;

// Interface CLI — consulta BD diretamente e pede análises ao Servidor via TCP
// O Servidor é que invoca o ServicoAnalise via gRPC (conforme enunciado TP2)
var settings = new AppSettings();
var repository = new SqliteMedicaoRepository(settings.DbPath);
var servidorService = new ServidorService(repository);
var tcpClient = new ServidorTcpClient(settings.ServidorHost, settings.ServidorPorta);

var cli = new CliApp(servidorService, tcpClient, settings.DbPath);
await cli.ExecutarAsync();

