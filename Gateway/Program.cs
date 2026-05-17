using Common.DataAccess;
using Gateway;
using Gateway.RpcClient;
using Gateway.ServerConnection;
using Gateway.Services;
using Gateway.Subscriber;

// Gateway TP2 — subscrição RabbitMQ + RPC pré-processamento + TCP servidor
GatewayConfig config = GatewayConfig.FromArgs(args);

var sensorRepo = new CsvSensorRegistoRepository(config.FicheiroSensores);
using var preProcClient = new PreProcessamentoGrpcClient();
using var forwarder = new ServerForwarder();
using var subscriber = new RabbitMqSubscriber();
var heartbeatMonitor = new HeartbeatMonitor(sensorRepo, config.TimeoutHeartbeatSegundos);

var gateway = new GatewayService(
    config,
    sensorRepo,
    preProcClient,
    forwarder,
    subscriber,
    heartbeatMonitor);

gateway.Iniciar();
