using Sensor;
using Sensor.Publisher;

// Ponto de entrada do sensor urbano (TP2 — publicação Pub/Sub via RabbitMQ)
SensorConfig config = SensorConfig.FromArgs(args);
using var publisher = new RabbitMqPublisher();
var app = new SensorApp(config, publisher);
app.Executar();
