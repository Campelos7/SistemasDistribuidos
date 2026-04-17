using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using System.Globalization;

class Servidor
{
    static string dbPath = "medicoes.db";
    static Mutex mutexDB = new Mutex();

    static void InicializarDB()
    {
        mutexDB.WaitOne();
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Reconstrução forçada devido à mudança de paradigma de dados
            new SqliteCommand("DROP TABLE IF EXISTS medicoes", conn).ExecuteNonQuery();
            new SqliteCommand("DROP TABLE IF EXISTS sensores", conn).ExecuteNonQuery();
            new SqliteCommand("DROP TABLE IF EXISTS alertas", conn).ExecuteNonQuery();

            // Tabela 1: Identidades de Sensores
            string sqlSensores = @"
                CREATE TABLE sensores (
                    sensor_id TEXT PRIMARY KEY,
                    zona      TEXT NOT NULL,
                    estado    TEXT NOT NULL
                )";
            new SqliteCommand(sqlSensores, conn).ExecuteNonQuery();

            // Tabela 2: Medições Reais (Com foreign key)
            string sqlMedicoes = @"
                CREATE TABLE medicoes (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp      TEXT NOT NULL,
                    sensor_id_fk   TEXT NOT NULL,
                    tipo_dado      TEXT NOT NULL,
                    valor_texto    TEXT NOT NULL,
                    valor_numerico REAL,
                    FOREIGN KEY(sensor_id_fk) REFERENCES sensores(sensor_id)
                )";
            new SqliteCommand(sqlMedicoes, conn).ExecuteNonQuery();

            // Tabela 3: Alertas Criticos
            string sqlAlertas = @"
                CREATE TABLE alertas (
                    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp          TEXT NOT NULL,
                    sensor_id_fk       TEXT NOT NULL,
                    mensagem_alerta    TEXT NOT NULL,
                    FOREIGN KEY(sensor_id_fk) REFERENCES sensores(sensor_id)
                )";
            new SqliteCommand(sqlAlertas, conn).ExecuteNonQuery();

            // Indíces de Performance
            new SqliteCommand("CREATE INDEX idx_fk_sensor ON medicoes(sensor_id_fk)", conn).ExecuteNonQuery();

            Console.WriteLine("[SERVIDOR] Base de dados Relacional IoT inicializada limpa (3 Tabelas).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro ao inicializar base de dados: {ex.Message}");
        }
        finally
        {
            mutexDB.ReleaseMutex();
        }
    }

    static void RegistarSensorSync(string sensorId, string zona, string estado)
    {
        mutexDB.WaitOne();
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            string sql = @"
                INSERT OR REPLACE INTO sensores (sensor_id, zona, estado)
                VALUES (@id, @zona, @est)";
            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", sensorId);
            cmd.Parameters.AddWithValue("@zona", zona);
            cmd.Parameters.AddWithValue("@est", estado);
            cmd.ExecuteNonQuery();
            Console.WriteLine($"[DB] Identidade Sincronizada: {sensorId} ({zona}) estado:{estado}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro sync sensor: {ex.Message}");
        }
        finally
        {
            mutexDB.ReleaseMutex();
        }
    }

    static void GerarAlerta(string timestamp, string sensorId, string mensagem)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            string sql = @"
                INSERT INTO alertas (timestamp, sensor_id_fk, mensagem_alerta)
                VALUES (@ts, @id, @msg)";
            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ts", timestamp);
            cmd.Parameters.AddWithValue("@id", sensorId);
            cmd.Parameters.AddWithValue("@msg", mensagem);
            cmd.ExecuteNonQuery();
            
            // Console display visual do alerta vermelho
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  🚨 [ALERTA CRÍTICO DB] Sensor:{sensorId} || {mensagem}\n");
            Console.ResetColor();
        }
        catch { }
    }

    static bool GuardarMedicao(string timestamp, string sensorId, string zona, string tipoDado, string valorTexto)
    {
        // 1º Tentar garantir agressivamente que o Sensor existe (Fallback passivo caso Gateway não tenha mandado Sync)
        RegistarSensorSync(sensorId, zona, "ativo_implicito"); 

        double? valorNumerico = null;
        
        // C# culture info invariant permite o parse independentemente de . ou , no input
        valorTexto = valorTexto.Replace(',', '.'); 
        if (double.TryParse(valorTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVal))
        {
            valorNumerico = parsedVal;
        }

        mutexDB.WaitOne();
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            
            string sql = @"
                INSERT INTO medicoes (timestamp, sensor_id_fk, tipo_dado, valor_texto, valor_numerico)
                VALUES (@ts, @sid, @tipo, @vtext, @vnum)";
            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ts", timestamp);
            cmd.Parameters.AddWithValue("@sid", sensorId);
            cmd.Parameters.AddWithValue("@tipo", tipoDado);
            cmd.Parameters.AddWithValue("@vtext", valorTexto);
            
            if (valorNumerico.HasValue) cmd.Parameters.AddWithValue("@vnum", valorNumerico.Value);
            else cmd.Parameters.AddWithValue("@vnum", DBNull.Value);
            
            cmd.ExecuteNonQuery();
            Console.WriteLine($"[SERVIDOR] Guardado med: {sensorId} | {tipoDado} | TEXTO:{valorTexto} | NUM:{valorNumerico} | {timestamp}");

            // Thresholds Inteligentes
            if (valorNumerico.HasValue)
            {
                if (tipoDado == "TEMP" && valorNumerico.Value > 35)
                {
                    GerarAlerta(timestamp, sensorId, $"Temperatura anormal detetada: {valorNumerico.Value}ºC");
                }
                else if (tipoDado == "PM2.5" && valorNumerico.Value > 50)
                {
                    GerarAlerta(timestamp, sensorId, $"Poluição Perigosa detetada PM2.5: {valorNumerico.Value}µg/m³");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro DB Medição: {ex.Message}");
            return false;
        }
        finally
        {
            mutexDB.ReleaseMutex();
        }
    }

    static void TratarGateway(object obj)
    {
        TcpClient cliente = (TcpClient)obj;
        string enderecoGateway = cliente.Client.RemoteEndPoint?.ToString() ?? "desconhecido";
        Console.WriteLine($"[SERVIDOR] Gateway {enderecoGateway} conectado.");

        NetworkStream stream = cliente.GetStream();
        StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        try
        {
            string linha;
            while ((linha = reader.ReadLine()) != null)
            {
                string[] partes = linha.Split('|');

                if (partes[0] == "SYNC_NODE" && partes.Length == 4)
                {
                    // SYNC_NODE|S101|ZONA_CENTRO|ativo
                    RegistarSensorSync(partes[1], partes[2], partes[3]);
                    writer.WriteLine("ACK_SYNC");
                }
                else if (partes[0] == "DATA" && partes.Length == 6)
                {
                    string sensorId = partes[1];
                    string zona = partes[2];
                    string tipoDado = partes[3];
                    string valor = partes[4];
                    string timestamp = partes[5];

                    if (string.IsNullOrWhiteSpace(sensorId) ||
                        string.IsNullOrWhiteSpace(zona) ||
                        string.IsNullOrWhiteSpace(tipoDado) ||
                        string.IsNullOrWhiteSpace(valor) ||
                        string.IsNullOrWhiteSpace(timestamp))
                    {
                        writer.WriteLine("ERROR");
                        continue;
                    }

                    bool sucesso = GuardarMedicao(timestamp, sensorId, zona, tipoDado, valor);
                    writer.WriteLine(sucesso ? "ACK" : "ERROR");
                }
                else
                {
                    writer.WriteLine("ERROR");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro na thread do gateway {enderecoGateway}: {ex.Message}");
        }
        finally
        {
            cliente.Close();
            Console.WriteLine($"[SERVIDOR] Gateway {enderecoGateway} desconectado.");
        }
    }

    static void Main(string[] args)
    {
        InicializarDB();

        TcpListener listener = new TcpListener(IPAddress.Any, 6000);
        listener.Start();
        Console.WriteLine("[SERVIDOR] A escutar gateways na porta 6000...");

        while (true)
        {
            try
            {
                TcpClient clienteGateway = listener.AcceptTcpClient();
                Thread t = new Thread(TratarGateway);
                t.IsBackground = true;
                t.Start(clienteGateway);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVIDOR] Erro ao aceitar ligação: {ex.Message}");
            }
        }
    }
}