using Common.Config;
using InterfaceVisualizacao;

// Interface CLI — cliente puro de rede.
// NÃO acede à base de dados: todas as consultas e análises são pedidas ao
// Servidor via TCP, e é o Servidor que invoca o ServicoAnalise via gRPC.
var settings = new AppSettings();
var tcpClient = new ServidorTcpClient(settings.ServidorHost, settings.ServidorPorta);

var cli = new CliApp(tcpClient, $"{settings.ServidorHost}:{settings.ServidorPorta}");
await cli.ExecutarAsync();
