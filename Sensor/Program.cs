using Common.Config;
using Common.Messaging;
using Sensor;
using Sensor.Publisher;

// Ponto de entrada do sensor urbano (TP2 — publicação Pub/Sub via RabbitMQ)
var settings = new AppSettings();
var routingKeys = new RoutingKeys();
var rabbitFactory = new RabbitMqConnectionFactory(settings);

var config = new SensorConfig(args);
using var publisher = new RabbitMqPublisher(rabbitFactory, settings.ExchangeMonitorizacao);
var app = new SensorApp(config, publisher, routingKeys);
app.Executar();
