using Common.Config;
using Common.DataAccess;
using Common.Messaging;
using Common.Serialization;
using Gateway;
using Gateway.RpcClient;
using Gateway.ServerConnection;
using Gateway.Services;
using Gateway.Subscriber;

// Gateway TP2 — subscrição RabbitMQ + RPC pré-processamento + TCP servidor
var settings = new AppSettings();
var routingKeys = new RoutingKeys();
var config = new GatewayConfig(args);

var rabbitFactory = new RabbitMqConnectionFactory(settings);
var sensorRepo = new CsvSensorRegistoRepository(config.FicheiroSensores);
using var preProcClient = new PreProcessamentoGrpcClient(settings.PreProcessamentoUrl);
using var forwarder = new ServerForwarder(settings.ServidorHost, settings.ServidorPorta);
using var subscriber = new RabbitMqSubscriber(rabbitFactory, settings.ExchangeMonitorizacao, routingKeys);
var heartbeatMonitor = new HeartbeatMonitor(sensorRepo, config.TimeoutHeartbeatSegundos);
var parserFactory = new FormatParserFactory();

var gateway = new GatewayService(
    config,
    sensorRepo,
    preProcClient,
    forwarder,
    subscriber,
    heartbeatMonitor,
    parserFactory);

gateway.Iniciar();
