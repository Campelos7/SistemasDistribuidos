using System;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;

class Sensor
{
    static StreamWriter writer;
    static StreamReader reader;
    static TcpClient cliente;
    static volatile bool emExecucao = true;
    static Mutex mutexSocket = new Mutex();

    static void Main(string[] args)
    {
        string sensorId = args.Length > 0 ? args[0] : "S101";
        string gatewayIP = args.Length > 1 ? args[1] : "127.0.0.1";

        try
        {
            cliente = new TcpClient(gatewayIP, 5000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SENSOR] Erro ao ligar ao Gateway em {gatewayIP}:5000 -> {ex.Message}");
            return;
        }

        NetworkStream stream = cliente.GetStream();
        writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        reader = new StreamReader(stream, Encoding.UTF8);
        Console.WriteLine($"[SENSOR] {sensorId} ligado ao Gateway em {gatewayIP}.");

        // --- HELLO ---
        Console.Write($"[SENSOR] Tipos de dados suportados (ex: TEMP,HUM,RUIDO): ");
        string tipos = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(tipos)) tipos = "TEMP";

        Console.Write($"[SENSOR] Zona do sensor: ");
        string zona = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(zona)) zona = "ZONA_DESCONHECIDA";

        try
        {
            writer.WriteLine($"HELLO|{sensorId}|{zona}|{tipos}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SENSOR] Erro ao enviar HELLO: {ex.Message}");
            cliente.Close();
            return;
        }

        string resposta;
        try
        {
            resposta = reader.ReadLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SENSOR] Erro ao ler resposta ao HELLO: {ex.Message}");
            cliente.Close();
            return;
        }

        Console.WriteLine($"[SENSOR] Resposta ao HELLO: {resposta}");

        if (resposta != "ACK")
        {
            Console.WriteLine("[SENSOR] Não autorizado pelo Gateway. A encerrar.");
            cliente.Close();
            return;
        }

        // --- Thread de Heartbeat ---
        Thread heartbeatThread = new Thread(() =>
        {
            while (emExecucao)
            {
                Thread.Sleep(30000);
                if (!emExecucao) break;

                mutexSocket.WaitOne();
                try
                {
                    writer.WriteLine($"HEARTBEAT|{sensorId}");
                    Console.WriteLine("[SENSOR] Heartbeat enviado.");
                    string ackHB = reader.ReadLine();
                    if (ackHB == null)
                    {
                        Console.WriteLine("[SENSOR] Gateway fechou a ligação durante heartbeat.");
                        emExecucao = false;
                    }
                    else
                    {
                        Console.WriteLine($"[SENSOR] Resposta ao heartbeat: {ackHB}");
                    }
                }
                catch (Exception ex)
                {
                    if (emExecucao)
                        Console.WriteLine($"[SENSOR] Erro no heartbeat: {ex.Message}");
                    emExecucao = false;
                }
                finally
                {
                    mutexSocket.ReleaseMutex();
                }
            }
        });
        heartbeatThread.IsBackground = true;
        heartbeatThread.Start();

        // --- Interface de texto ---
        Console.WriteLine("\n=== Comandos disponíveis ===");
        Console.WriteLine("  data <tipo> <valor>   (ex: data TEMP 22.5)"); 
        Console.WriteLine("  bye                    (terminar ligação)");
        Console.WriteLine("  help                   (mostrar ajuda)");
        Console.WriteLine("==========================\n");

        while (emExecucao)
        {
            Console.Write("> ");
            string input = Console.ReadLine();
            if (input == null) break;
            input = input.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            string[] partes = input.Split(' ', 3);

            if (partes[0].ToLower() == "data" && partes.Length == 3)
            {
                string tipo = partes[1].ToUpper();
                string valor = partes[2];
                string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

                mutexSocket.WaitOne();
                try
                {
                    writer.WriteLine($"DATA|{sensorId}|{tipo}|{valor}|{timestamp}");
                    string resp = reader.ReadLine();
                    if (resp == null)
                    {
                        Console.WriteLine("[SENSOR] Gateway fechou a ligação.");
                        emExecucao = false;
                    }
                    else
                    {
                        Console.WriteLine($"[SENSOR] Resposta: {resp}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SENSOR] Erro ao enviar DATA: {ex.Message}");
                    emExecucao = false;
                }
                finally
                {
                    mutexSocket.ReleaseMutex();
                }
            }
            else if (partes[0].ToLower() == "bye")
            {
                emExecucao = false;
                mutexSocket.WaitOne();
                try
                {
                    writer.WriteLine($"BYE|{sensorId}");
                    string resp = reader.ReadLine();
                    Console.WriteLine($"[SENSOR] Resposta: {resp}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SENSOR] Erro ao enviar BYE: {ex.Message}");
                }
                finally
                {
                    mutexSocket.ReleaseMutex();
                }
                break;
            }
            else if (partes[0].ToLower() == "help")
            {
                Console.WriteLine("  data <tipo> <valor>   envia uma medição (ex: data TEMP 22.5)");
                Console.WriteLine("  bye                   termina a ligação");
            }
            else
            {
                Console.WriteLine("Comando inválido. Usa: data <tipo> <valor>  |  bye  |  help");
            }
        }

        cliente.Close();
        Console.WriteLine("[SENSOR] Desligado.");
    }
}