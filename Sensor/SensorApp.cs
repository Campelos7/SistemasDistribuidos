using Common.Messaging;
using Common.Models;
using Sensor.Publisher;

namespace Sensor;

/// <summary>
/// Aplicação do sensor: interface CLI e publicação Pub/Sub no RabbitMQ.
/// </summary>
public class SensorApp
{
    private readonly SensorConfig _config;
    private readonly RabbitMqPublisher _publisher;
    private readonly RoutingKeys _routingKeys;
    private CancellationTokenSource? _heartbeatCts;

    public SensorApp(SensorConfig config, RabbitMqPublisher publisher, RoutingKeys routingKeys)
    {
        _config = config;
        _publisher = publisher;
        _routingKeys = routingKeys;
    }

    /// <summary>
    /// Inicia o sensor: registo, heartbeat em background e loop de comandos.
    /// </summary>
    public void Executar()
    {
        PublicarRegisto();
        IniciarHeartbeat();

        Console.WriteLine($"[SENSOR {_config.SensorId}] Ligado ao RabbitMQ. Zona: {_config.Zona}");
        Console.WriteLine("\nComandos:");
        Console.WriteLine("  data <tipo> <valor>              (ex: data PM2.5 78)");
        Console.WriteLine("  datajson <json>                  (publica payload JSON)");
        Console.WriteLine("  dataxml <tipo> <valor>           (publica payload XML)");
        Console.WriteLine("  datacsv <tipo> <valor>           (publica payload CSV)");
        Console.WriteLine("  bye");

        while (true)
        {
            Console.Write("> ");
            string? input = Console.ReadLine();
            if (input == null) continue;

            string[] partes = input.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0) continue;

            switch (partes[0].ToLowerInvariant())
            {
                case "data" when partes.Length == 3:
                    PublicarMedicao(partes[1], double.Parse(partes[2], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "datajson" when partes.Length >= 2:
                    PublicarMedicaoJson(partes[1] + (partes.Length > 2 ? " " + partes[2] : ""));
                    break;
                case "dataxml" when partes.Length == 3:
                    PublicarMedicaoXml(partes[1], double.Parse(partes[2], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "datacsv" when partes.Length == 3:
                    PublicarMedicaoCsv(partes[1], double.Parse(partes[2], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "bye":
                    _heartbeatCts?.Cancel();
                    Console.WriteLine("[SENSOR] Encerrado.");
                    return;
                default:
                    Console.WriteLine("Comando inválido.");
                    break;
            }
        }
    }

    private void PublicarRegisto()
    {
        var msg = new MensagemPubSub
        {
            Tipo = "registo",
            SensorId = _config.SensorId,
            Zona = _config.Zona,
            TiposSuportados = _config.TiposSuportados.ToList(),
            Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
        };
        string key = _routingKeys.Registo(_config.Zona, _config.SensorId);
        _publisher.Publicar(key, msg);
        Console.WriteLine("[SENSOR] Registo publicado no broker.");
    }

    private void PublicarMedicao(string tipo, double valor)
    {
        var msg = new MensagemPubSub
        {
            Tipo = "medicao",
            SensorId = _config.SensorId,
            Zona = _config.Zona,
            TipoDado = tipo,
            Valor = valor,
            Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            Formato = "NONE"
        };
        string key = _routingKeys.Medicao(_config.Zona, tipo);
        _publisher.Publicar(key, msg);
        Console.WriteLine($"[SENSOR] Medição publicada: {tipo}={valor}");
    }

    private void PublicarMedicaoJson(string json)
    {
        var msg = new MensagemPubSub
        {
            Tipo = "medicao",
            SensorId = _config.SensorId,
            Zona = _config.Zona,
            Formato = "JSON",
            Payload = json,
            Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
        };
        string key = _routingKeys.Medicao(_config.Zona, "JSON");
        _publisher.Publicar(key, msg);
        Console.WriteLine("[SENSOR] Medição JSON publicada.");
    }

    private void PublicarMedicaoXml(string tipo, double valor)
    {
        string ts = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        string v = valor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string xml =
            "<medicao>" +
            $"<sensorId>{_config.SensorId}</sensorId>" +
            $"<zona>{_config.Zona}</zona>" +
            $"<tipoDado>{tipo}</tipoDado>" +
            $"<valor>{v}</valor>" +
            $"<timestamp>{ts}</timestamp>" +
            "</medicao>";

        var msg = new MensagemPubSub
        {
            Tipo = "medicao",
            SensorId = _config.SensorId,
            Zona = _config.Zona,
            Formato = "XML",
            Payload = xml,
            Timestamp = ts
        };
        _publisher.Publicar(_routingKeys.Medicao(_config.Zona, "XML"), msg);
        Console.WriteLine($"[SENSOR] Medição XML publicada: {tipo}={valor}");
    }

    private void PublicarMedicaoCsv(string tipo, double valor)
    {
        string ts = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        string v = valor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string csv =
            "sensorId,zona,tipoDado,valor,timestamp\n" +
            $"{_config.SensorId},{_config.Zona},{tipo},{v},{ts}";

        var msg = new MensagemPubSub
        {
            Tipo = "medicao",
            SensorId = _config.SensorId,
            Zona = _config.Zona,
            Formato = "CSV",
            Payload = csv,
            Timestamp = ts
        };
        _publisher.Publicar(_routingKeys.Medicao(_config.Zona, "CSV"), msg);
        Console.WriteLine($"[SENSOR] Medição CSV publicada: {tipo}={valor}");
    }

    private void IniciarHeartbeat()
    {
        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                var msg = new MensagemPubSub
                {
                    Tipo = "heartbeat",
                    SensorId = _config.SensorId,
                    Zona = _config.Zona,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
                };
                _publisher.Publicar(_routingKeys.Heartbeat(_config.Zona, _config.SensorId), msg);
                Console.WriteLine("[SENSOR] Heartbeat publicado.");
            }
        }, token);
    }
}
