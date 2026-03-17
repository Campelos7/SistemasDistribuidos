using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Threading;

class Gateway
{
    static StreamWriter writerServidor;
    static StreamReader readerServidor;
    static string ficheiroCSV = "sensores.csv";

    static Mutex mutexCSV = new Mutex();
    static Mutex mutexServidor = new Mutex();

    // Timeout em segundos (90 segundos = 3 heartbeats perdidos)
    static int timeoutSegundos = 90;

    class InfoSensor
    {
        public string Estado;
        public string Zona;
        public List<string> Tipos;
        public string LastSync;
    }

    static Dictionary<string, InfoSensor> CarregarCSV()
    {
        var sensores = new Dictionary<string, InfoSensor>();
        if (!File.Exists(ficheiroCSV)) return sensores;

        foreach (string linha in File.ReadAllLines(ficheiroCSV))
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;
            string[] partes = linha.Split(':');
            if (partes.Length < 5) continue;

            string tiposStr = partes[3].Trim('[', ']');
            sensores[partes[0]] = new InfoSensor
            {
                Estado = partes[1],
                Zona = partes[2],
                Tipos = new List<string>(tiposStr.Split(',')),
                LastSync = partes[4]
            };
        }
        return sensores;
    }

    static void AtualizarLastSync(string sensorId)
    {
        mutexCSV.WaitOne();
        try
        {
            if (!File.Exists(ficheiroCSV)) return;
            string[] linhas = File.ReadAllLines(ficheiroCSV);
            string agora = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            for (int i = 0; i < linhas.Length; i++)
            {
                if (linhas[i].StartsWith(sensorId + ":"))
                {
                    string[] partes = linhas[i].Split(':');
                    if (partes.Length >= 5) { partes[4] = agora; linhas[i] = string.Join(":", partes); }
                }
            }
            File.WriteAllLines(ficheiroCSV, linhas);
        }
        finally { mutexCSV.ReleaseMutex(); }
    }

    static void MarcarSensorInativo(string sensorId)
    {
        mutexCSV.WaitOne();
        try
        {
            if (!File.Exists(ficheiroCSV)) return;
            string[] linhas = File.ReadAllLines(ficheiroCSV);
            for (int i = 0; i < linhas.Length; i++)
            {
                if (linhas[i].StartsWith(sensorId + ":"))
                {
                    string[] partes = linhas[i].Split(':');
                    if (partes.Length >= 5) { partes[1] = "desativado"; linhas[i] = string.Join(":", partes); }
                }
            }
            File.WriteAllLines(ficheiroCSV, linhas);
            Console.WriteLine($"[GATEWAY] Sensor {sensorId} marcado como desativado por timeout!");
        }
        finally { mutexCSV.ReleaseMutex(); }
    }

    // Thread que verifica heartbeats em background
    static void MonitorizarHeartbeats()
    {
        while (true)
        {
            Thread.Sleep(30000); // verifica a cada 30 segundos

            mutexCSV.WaitOne();
            Dictionary<string, InfoSensor> sensores;
            try { sensores = CarregarCSV(); }
            finally { mutexCSV.ReleaseMutex(); }

            foreach (var par in sensores)
            {
                string id = par.Key;
                InfoSensor info = par.Value;

                if (info.Estado != "ativo") continue;

                if (DateTime.TryParse(info.LastSync, out DateTime lastSync))
                {
                    double segundosPassados = (DateTime.Now - lastSync).TotalSeconds;
                    if (segundosPassados > timeoutSegundos)
                    {
                        Console.WriteLine($"[GATEWAY] Sensor {id} sem heartbeat há {(int)segundosPassados}s.");
                        MarcarSensorInativo(id);
                    }
                }
            }
        }
    }

    static void TratarSensor(object obj)
    {
        TcpClient clienteSensor = (TcpClient)obj;
        NetworkStream streamSensor = clienteSensor.GetStream();
        StreamReader readerSensor = new StreamReader(streamSensor, Encoding.UTF8);
        StreamWriter writerSensor = new StreamWriter(streamSensor, Encoding.UTF8) { AutoFlush = true };

        mutexCSV.WaitOne();
        Dictionary<string, InfoSensor> sensores;
        try { sensores = CarregarCSV(); }
        finally { mutexCSV.ReleaseMutex(); }

        string sensorId = "";
        string zona = "";
        string linha;

        while ((linha = readerSensor.ReadLine()) != null)
        {
            Console.WriteLine($"[GATEWAY] Recebido: {linha}");
            string[] partes = linha.Split('|');

            if (partes[0] == "HELLO" && partes.Length == 4)
            {
                sensorId = partes[1];
                if (!sensores.ContainsKey(sensorId))
                {
                    Console.WriteLine($"[GATEWAY] Sensor {sensorId} não registado.");
                    writerSensor.WriteLine("ERROR"); break;
                }
                if (sensores[sensorId].Estado != "ativo")
                {
                    Console.WriteLine($"[GATEWAY] Sensor {sensorId} está {sensores[sensorId].Estado}.");
                    writerSensor.WriteLine("ERROR"); break;
                }
                zona = sensores[sensorId].Zona;
                Console.WriteLine($"[GATEWAY] Sensor {sensorId} validado. Zona: {zona}");
                AtualizarLastSync(sensorId);
                writerSensor.WriteLine("ACK");
            }
            else if (partes[0] == "DATA" && partes.Length == 5)
            {
                string tipoDado = partes[2];
                string valor = partes[3];
                string timestamp = partes[4];

                if (sensores.ContainsKey(sensorId) && !sensores[sensorId].Tipos.Contains(tipoDado))
                {
                    Console.WriteLine($"[GATEWAY] Tipo {tipoDado} não suportado.");
                    writerSensor.WriteLine("ERROR"); continue;
                }

                mutexServidor.WaitOne();
                try
                {
                    string msg = $"DATA|{sensorId}|{zona}|{tipoDado}|{valor}|{timestamp}";
                    writerServidor.WriteLine(msg);
                    string resp = readerServidor.ReadLine();
                    Console.WriteLine($"[GATEWAY] Servidor respondeu: {resp}");
                    AtualizarLastSync(sensorId);
                    writerSensor.WriteLine(resp);
                }
                finally { mutexServidor.ReleaseMutex(); }
            }
            else if (partes[0] == "HEARTBEAT" && partes.Length == 2)
            {
                Console.WriteLine($"[GATEWAY] Heartbeat de {partes[1]}.");
                AtualizarLastSync(partes[1]);
                writerSensor.WriteLine("ACK");
            }
            else if (partes[0] == "BYE" && partes.Length == 2)
            {
                Console.WriteLine($"[GATEWAY] Sensor {partes[1]} desligou.");
                writerSensor.WriteLine("ACK"); break;
            }
            else
            {
                writerSensor.WriteLine("ERROR");
            }
        }

        clienteSensor.Close();
        Console.WriteLine($"[GATEWAY] Thread do sensor {sensorId} encerrada.");
    }

    static void Main(string[] args)
    {
        TcpClient clienteServidor = new TcpClient("127.0.0.1", 6000);
        NetworkStream streamServidor = clienteServidor.GetStream();
        writerServidor = new StreamWriter(streamServidor, Encoding.UTF8) { AutoFlush = true };
        readerServidor = new StreamReader(streamServidor, Encoding.UTF8);
        Console.WriteLine("[GATEWAY] Ligado ao Servidor.");

        // Arranca thread de monitorização de heartbeats
        Thread monitorThread = new Thread(MonitorizarHeartbeats);
        monitorThread.IsBackground = true;
        monitorThread.Start();
        Console.WriteLine("[GATEWAY] Monitorização de heartbeats ativa.");

        TcpListener listener = new TcpListener(IPAddress.Any, 5000);
        listener.Start();
        Console.WriteLine("[GATEWAY] A escutar sensores na porta 5000...");

        while (true)
        {
            TcpClient clienteSensor = listener.AcceptTcpClient();
            Thread t = new Thread(TratarSensor);
            t.Start(clienteSensor);
        }
    }
}