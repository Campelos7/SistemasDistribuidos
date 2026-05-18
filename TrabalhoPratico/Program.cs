using Common.Config;
using Common.DataAccess;
using Common.RpcClient;
using Common.Services;
using Servidor.Networking;

// Servidor principal TP2 — TCP (gateways) + SQLite + cliente RPC análise
var settings = new AppSettings();
var repository = new SqliteMedicaoRepository(settings.DbPath);
using var analiseClient = new AnaliseGrpcClient(settings.AnaliseUrl);
var servidorService = new ServidorService(repository, analiseClient);

var listener = new GatewayTcpListener(servidorService, settings.ServidorPorta);
listener.Iniciar();
