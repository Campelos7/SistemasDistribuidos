using System;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;
using System.Collections.Generic;

class Sensor
{
    static StreamWriter writer;
    static StreamReader reader;
    static TcpClient cliente;
    static volatile bool emExecucao = true;
    static Mutex mutexSocket = new Mutex();

    static string ficheiroCSV = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Gateway", "bin", "Debug", "net8.0", "sensores.csv");

    class InfoSensor
    {
        public string Estado;
        public string Zona;
        public Dictionary<string, string> ValoresRegistados;
        public List<string> Tipos;
    }

    static Dictionary<string, InfoSensor> CarregarCSV()
    {
        var sensores = new Dictionary<string, InfoSensor>();
        
        // Tentativa de descobrir o ficheiro se o caminho base falhar
        string caminhoLocal = "sensores.csv";
        string caminhoEfetivo = ficheiroCSV;
        if (!File.Exists(caminhoEfetivo))
        {
            if (File.Exists(caminhoLocal)) caminhoEfetivo = caminhoLocal;
            else return sensores; // Ficheiro não existe de todo
        }

        foreach (string linha in File.ReadAllLines(caminhoEfetivo))
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;
            string[] partes = linha.Split(':', 5);
            if (partes.Length < 5) continue;

            string tiposStr = partes[3].Trim('[', ']');
            List<string> tipos = new List<string>();
            var valores = new Dictionary<string, string>();
            
            foreach (string t in tiposStr.Split(','))
            {
                string[] keyVal = t.Split('=', 2);
                string tt = keyVal[0].Trim();
                if (!string.IsNullOrEmpty(tt)) 
                {
                    tipos.Add(tt);
                    if (keyVal.Length > 1) 
                    {
                        string val = keyVal[1].Trim();
                        // Guardar valor apenas se não for marcador null e não for vazio
                        if (val.ToLower() != "null" && !string.IsNullOrEmpty(val)) 
                            valores[tt] = val;
                    }
                }
            }

            sensores[partes[0].Trim()] = new InfoSensor
            {
                Estado = partes[1].Trim(),
                Zona = partes[2].Trim(),
                Tipos = tipos,
                ValoresRegistados = valores
            };
        }
        return sensores;
    }

    static void GuardarNovoSensor(string sensorId, string zona, List<string> tipos)
    {
        string caminhoEfetivo = File.Exists(ficheiroCSV) ? ficheiroCSV : "sensores.csv";
        
        List<string> blocos = new List<string>();
        foreach(var t in tipos) blocos.Add($"{t}=null");
        string novaLinha = $"{sensorId}:ativo:{zona}:[{string.Join(",", blocos)}]:{DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")}";
        File.AppendAllText(caminhoEfetivo, novaLinha + Environment.NewLine);
    }

    // ─────────────────────────────────────────────
    //  Menu Identity & Operations
    // ─────────────────────────────────────────────
    static string EscolherSensorIdentidade()
    {
        while (true)
        {
            var sensores = CarregarCSV();
            Console.WriteLine("\n╔══════════════════════════════════════╗");
            Console.WriteLine("║        ESCOLHA A SUA IDENTIDADE      ║");
            Console.WriteLine("╠══════════════════════════════════════╣");
            
            int c = 1;
            List<string> idsDisponiveis = new List<string>();
            foreach (var kvp in sensores)
            {
                if (kvp.Value.Estado == "ativo")
                {
                    Console.WriteLine($"║   {c}. {kvp.Key,-33}║");
                    idsDisponiveis.Add(kvp.Key);
                    c++;
                }
            }
            if (idsDisponiveis.Count == 0)
                Console.WriteLine("║   (Nenhum sensor ativo no CSV)       ║");

            Console.WriteLine("╠══════════════════════════════════════╣");
            Console.WriteLine("║   N. Criar Novo Sensor               ║");
            Console.WriteLine("╚══════════════════════════════════════╝");
            Console.Write("  Escolha uma opção: ");
            
            string opt = Console.ReadLine()?.Trim().ToUpper();
            if (opt == "N")
            {
                Console.Write("  Nome do Novo Sensor (ex: S104): ");
                string nId = Console.ReadLine()?.Trim();
                Console.Write("  Zona do Sensor (ex: ZONA_SUL): ");
                string nZona = Console.ReadLine()?.Trim();
                Console.Write("  Tipos Suportados (separados por vírgula, ex: TEMP,HUM): ");
                string nTipos = Console.ReadLine()?.Trim();
                
                if(!string.IsNullOrEmpty(nId) && !string.IsNullOrEmpty(nZona) && !string.IsNullOrEmpty(nTipos))
                {
                    List<string> mTipos = new List<string>();
                    foreach(var tx in nTipos.Split(',')) 
                    {
                        if(!string.IsNullOrWhiteSpace(tx)) mTipos.Add(tx.Trim().ToUpper());
                    }
                    GuardarNovoSensor(nId, nZona, mTipos);
                    Console.WriteLine($"  [✓] Sensor {nId} criado com sucesso e ativado!");
                }
                else Console.WriteLine("  [AVISO] Dados inválidos.");
                
                continue;
            }

            if (int.TryParse(opt, out int idx) && idx >= 1 && idx <= idsDisponiveis.Count)
            {
                return idsDisponiveis[idx - 1]; // Retorna a string do Sensor Escohido
            }
            Console.WriteLine("  [AVISO] Opção inválida.");
        }
    }

    static void MostrarSubMenu(string sensorId)
    {
        Console.WriteLine("\n╔══════════════════════════════════════╗");
        Console.WriteLine($"║   OPERAÇÕES (SENSOR: {sensorId,-14}) ║");
        Console.WriteLine("╠══════════════════════════════════════╣");
        Console.WriteLine("║   1. Carregar Dados do CSV           ║");
        Console.WriteLine("║   2. Introduzir Dados Manualmente    ║");
        Console.WriteLine("║   S. Simular Stream de Vídeo         ║");
        Console.WriteLine("╠══════════════════════════════════════╣");
        Console.WriteLine("║   0. Sair e Desligar (BYE)           ║");
        Console.WriteLine("╚══════════════════════════════════════╝");
        Console.Write("  Escolha uma opção: ");
    }

    static void MostrarMenuManual(List<string> tiposSuportados)
    {
        Console.WriteLine("\n╔══════════════════════════════════════╗");
        Console.WriteLine("║           MODO MANUAL                ║");
        Console.WriteLine("╠══════════════════════════════════════╣");
        Console.WriteLine("║  Seleciona o tipo de dado a enviar:  ║");
        Console.WriteLine("╠══════════════════════════════════════╣");
        for (int i = 0; i < tiposSuportados.Count; i++)
            Console.WriteLine($"║   {i + 1}. {tiposSuportados[i],-33}║");
        Console.WriteLine("╠══════════════════════════════════════╣");
        Console.WriteLine("║   0. Voltar ao Menu Anterior         ║");
        Console.WriteLine("╚══════════════════════════════════════╝");
        Console.Write("  Escolha uma opção: ");
    }

    // ─────────────────────────────────────────────
    //  Envios de Rede (Preservados do Original)
    // ─────────────────────────────────────────────
    
    static void DispararMedicaoParaGateway(string sensorId, string tipo, string valor)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        mutexSocket.WaitOne();
        try
        {
            writer.WriteLine($"DATA|{sensorId}|{tipo}|{valor}|{timestamp}");
            string resp = reader.ReadLine();

            if (resp == null)
            {
                Console.WriteLine("  [SENSOR] Gateway fechou a ligação.");
                emExecucao = false;
            }
            else if (resp == "ACK")
            {
                Console.WriteLine($"  [✓] Enviado: {tipo} = {valor}  ({timestamp})");
            }
            else
            {
                Console.WriteLine($"  [✗] Erro. Resposta do Gateway: {resp}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [SENSOR] Erro ao enviar DATA: {ex.Message}");
            emExecucao = false;
        }
        finally
        {
            mutexSocket.ReleaseMutex();
        }
    }

    static void EnviarMedicao(string sensorId, string tipo)
    {
        string valor = "";
        if (tipo == "IMAGEM" || tipo == "VIDEO")
        {
            Console.Write($"  Caminho do ficheiro local para {tipo}: ");
            string caminho = Console.ReadLine()?.Trim();
            
            if (caminho.StartsWith("\"") && caminho.EndsWith("\""))
                caminho = caminho.Substring(1, caminho.Length - 2);
            
            if (string.IsNullOrEmpty(caminho))
            {
                Console.WriteLine("  [AVISO] O caminho não pode estar vazio.");
                return;
            }

            if (!File.Exists(caminho))
            {
                Console.WriteLine("  [AVISO] Ficheiro não detetado. A simular envio...");
                valor = $"[Ficheiro Falso: {Path.GetFileName(caminho)}]";
            }
            else
            {
                long bytesTamanho = new FileInfo(caminho).Length;
                valor = $"[Load: {Path.GetFileName(caminho)} - Peso: {bytesTamanho} bytes]";
            }
        }
        else
        {
            Console.Write($"  Valor numérico/texto para {tipo}: ");
            valor = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(valor))
            {
                Console.WriteLine("  [AVISO] Valor não pode ser vazio.");
                return;
            }
        }

        DispararMedicaoParaGateway(sensorId, tipo, valor);
    }

    static void EnviarStreamVideo(string sensorId)
    {
        mutexSocket.WaitOne();
        try
        {
            writer.WriteLine($"STREAM_START|{sensorId}");
            string resp = reader.ReadLine();
            
            if (resp == "ACK")
            {
                Console.WriteLine("  [✓] Negociação STREAM_START concluída. A enviar frames...");
                for (int i = 1; i <= 5; i++)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                    string frameData = $"FRAME_PAYLOAD_{i}_BASE64...";
                    Console.WriteLine($"      A enviar frame {i}...");
                    writer.WriteLine($"STREAM_FRAME|{sensorId}|{frameData}|{timestamp}");
                    Thread.Sleep(500);
                }

                writer.WriteLine($"STREAM_STOP|{sensorId}");
                string stopResp = reader.ReadLine();
                Console.WriteLine($"  [SENSOR] Resposta STREAM_STOP: {stopResp}");
            }
            else
            {
                Console.WriteLine($"  [✗] Gateway recusou a stream: {resp}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [SENSOR] Erro ao enviar STREAM: {ex.Message}");
        }
        finally
        {
            mutexSocket.ReleaseMutex();
        }
    }

    static void EnviarBye(string sensorId)
    {
        emExecucao = false;
        mutexSocket.WaitOne();
        try
        {
            writer.WriteLine($"BYE|{sensorId}");
            string resp = reader.ReadLine();
            Console.WriteLine($"  [SENSOR] Resposta: {resp}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SENSOR] Erro ao enviar BYE: {ex.Message}");
        }
        finally
        {
            mutexSocket.ReleaseMutex();
        }
    }

    // ─────────────────────────────────────────────
    //  Main
    // ─────────────────────────────────────────────
    static void Main(string[] args)
    {
        Console.WriteLine("[SENSOR] Sistema de Sensor Edge - Inicialização");
        
        string sensorId = EscolherSensorIdentidade();
        
        var tabelaCSV = CarregarCSV();
        if (!tabelaCSV.ContainsKey(sensorId))
        {
            Console.WriteLine($"[SENSOR] Erro fatal: {sensorId} desapareceu do CSV.");
            return;
        }

        InfoSensor definicoesBase = tabelaCSV[sensorId];
        string zona = definicoesBase.Zona;
        List<string> tiposSuportados = definicoesBase.Tipos;

        string gatewayIP = "127.0.0.1";

        // ── Ligação ao Gateway ──
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

        // HELLO declara todos os tipos disponíveis deste sensor a partir do CSV
        string tiposDeclared = string.Join(",", tiposSuportados);
        try
        {
            writer.WriteLine($"HELLO|{sensorId}|{zona}|{tiposDeclared}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SENSOR] Erro ao enviar HELLO: {ex.Message}");
            cliente.Close();
            return;
        }

        string resposta;
        try { resposta = reader.ReadLine(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[SENSOR] Erro ao ler resposta ao HELLO: {ex.Message}");
            cliente.Close();
            return;
        }

        if (resposta != "ACK")
        {
            Console.WriteLine("[SENSOR] Não autorizado pelo Gateway. Prima qualquer tecla...");
            Console.ReadKey();
            cliente.Close();
            return;
        }

        Console.WriteLine($"[SENSOR] Gateway Autorizou. Sessão iniciada para a {zona}.");

        // ── Thread de Heartbeat ──
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
                    Console.WriteLine("\n  [SENSOR] Heartbeat enviado.");
                    string ackHB = reader.ReadLine();
                    if (ackHB == null) emExecucao = false;
                }
                catch { emExecucao = false; }
                finally { mutexSocket.ReleaseMutex(); }
            }
        });
        heartbeatThread.IsBackground = true;
        heartbeatThread.Start();

        // ── Loop do menu ──
        bool submenuAberto = true;
        while (emExecucao && submenuAberto)
        {
            MostrarSubMenu(sensorId);
            string opcSub = Console.ReadLine()?.Trim().ToUpper();
            
            if (opcSub == "0") 
            {
                EnviarBye(sensorId);
                break;
            }
            else if (opcSub == "S")
            {
                EnviarStreamVideo(sensorId);
            }
            else if (opcSub == "1") // Carregar Dados do CSV
            {
                var dictSensoresAtualizado = CarregarCSV();
                var meusDados = dictSensoresAtualizado[sensorId].ValoresRegistados;
                
                if (meusDados.Count == 0)
                {
                    Console.WriteLine("  [AVISO] Não existem dados gravados com valores válidos para este sensor no CSV.");
                    continue;
                }

                Console.WriteLine("\n  [INJEÇÃO] Iniciando upload massivo de histórico do CSV...");
                foreach(var d in meusDados)
                {
                    DispararMedicaoParaGateway(sensorId, d.Key, d.Value);
                    Thread.Sleep(750); // Simular throttling
                }
                Console.WriteLine("  [INJEÇÃO] Histórico enviado com sucesso.");
            }
            else if (opcSub == "2") // Introduzir Manualmente
            {
                while (emExecucao)
                {
                    MostrarMenuManual(tiposSuportados);
                    string optMan = Console.ReadLine()?.Trim();
                    
                    if (string.IsNullOrEmpty(optMan)) continue;
                    if (optMan == "0") break; // Volta ao SubMenu
                    
                    if (int.TryParse(optMan, out int indexOpt) && indexOpt >= 1 && indexOpt <= tiposSuportados.Count)
                    {
                        EnviarMedicao(sensorId, tiposSuportados[indexOpt - 1]);
                    }
                    else
                    {
                        Console.WriteLine("  [AVISO] Insere um número válido.");
                    }
                }
            }
            else
            {
                 Console.WriteLine("  [AVISO] Opção inválida.");
            }
        }

        if(emExecucao) cliente.Close(); // Por precaução
        Console.WriteLine("[SENSOR] Desligado.");
    }
}