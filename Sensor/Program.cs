using System;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;

class Sensor
{
    static StreamWriter writer;
    static StreamReader reader;

    static void Main(string[] args)
    {
        string sensorId = "S102";
        string zona = "ZONA_ESCOLAR";
        string tipos = "PM2.5,TEMP,RUIDO";

        // Liga ao Gateway
        TcpClient cliente = new TcpClient("127.0.0.1", 5000);
        NetworkStream stream = cliente.GetStream();
        writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        reader = new StreamReader(stream, Encoding.UTF8);
        Console.WriteLine("[SENSOR] Ligado ao Gateway.");

        // Envia HELLO
        writer.WriteLine($"HELLO|{sensorId}|{zona}|{tipos}");
        string resposta = reader.ReadLine();
        Console.WriteLine($"[SENSOR] Resposta ao HELLO: {resposta}");

        if (resposta != "ACK")
        {
            Console.WriteLine("[SENSOR] Não autorizado. A encerrar.");
            cliente.Close();
            return;
        }

        // Thread para enviar heartbeat a cada 30 segundos
        Thread heartbeat = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(30000);
                writer.WriteLine($"HEARTBEAT|{sensorId}");
                Console.WriteLine("[SENSOR] Heartbeat enviado.");
            }
        });
        heartbeat.IsBackground = true;
        heartbeat.Start();

        // Interface de texto simples
        Console.WriteLine("\nComandos disponíveis:");
        Console.WriteLine("  data <tipo> <valor>   (ex: data PM2.5 78)");
        Console.WriteLine("  bye");

        while (true)
        {
            Console.Write("> ");
            string input = Console.ReadLine();
            if (input == null) continue;

            string[] partes = input.Trim().Split(' ');

            if (partes[0].ToLower() == "data" && partes.Length == 3)
            {
                string tipo = partes[1];
                string valor = partes[2];
                string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

                writer.WriteLine($"DATA|{sensorId}|{tipo}|{valor}|{timestamp}");
                string resp = reader.ReadLine();
                Console.WriteLine($"[SENSOR] Resposta: {resp}");
            }
            else if (partes[0].ToLower() == "bye")
            {
                writer.WriteLine($"BYE|{sensorId}");
                string resp = reader.ReadLine();
                Console.WriteLine($"[SENSOR] Resposta: {resp}");
                break;
            }
            else
            {
                Console.WriteLine("Comando inválido. Usa: data <tipo> <valor>  ou  bye");
            }
        }

        cliente.Close();
        Console.WriteLine("[SENSOR] Desligado.");
    }
}