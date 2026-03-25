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

    // Flag para sinalizar ao heartbeat que deve parar
    static volatile bool emExecucao = true;
    // Mutex para proteger escritas no socket (DATA e HEARTBEAT podem colidir)
    static Mutex mutexSocket = new Mutex();

    static void Main(string[] args)
    {
        // Permite passar o ID como argumento, ou usa "S101" por defeito
        string sensorId = args.Length > 0 ? args[0] : "S101";
        // IP do gateway como segundo argumento, ou localhost por defeito
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
        // O sensor indica os tipos que consegue recolher.
        // O gateway valida contra o CSV, mas enviamos mesmo assim conforme o protocolo.
        Console.Write($"[SENSOR] Tipos de dados suportados (ex: TEMP,HUM,RUIDO): ");
        string tipos = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(tipos)) tipos = "TEMP";

        // Zona não é necessária aqui (o gateway lê do CSV), mas o protocolo prevê o campo
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
        // Envia heartbeat a cada 30 segundos enquanto emExecucao for true.
        // Usa mutex para não colidir com envios de DATA.
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

                    // Lê o ACK do heartbeat
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
            Console.WriteLine("[SENSOR] Thread de heartbeat encerrada.");
        });
        heartbeatThread.IsBackground = true;
        heartbeatThread.Start();

        // --- Interface de texto ---
        Console.WriteLine("\nComandos disponíveis:");
        Console.WriteLine("  data <tipo> <valor>   (ex: data TEMP 22.5)");
        Console.WriteLine("  bye");
        Console.WriteLine("  help");

        while (emExecucao)
        {
            Console.Write("> ");
            string input = Console.ReadLine();

            if (input == null) break; // EOF (ex: pipe fechado)
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
                emExecucao = false; // Para o heartbeat antes de enviar BYE

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