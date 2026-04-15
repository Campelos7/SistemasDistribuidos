using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;

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

            string sqlMedicoes = @"
                CREATE TABLE IF NOT EXISTS medicoes (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp  TEXT NOT NULL,
                    sensor_id  TEXT NOT NULL,
                    zona       TEXT NOT NULL,
                    tipo_dado  TEXT NOT NULL,
                    valor      TEXT NOT NULL
                )";
            new SqliteCommand(sqlMedicoes, conn).ExecuteNonQuery();

            string sqlIdx = @"
                CREATE INDEX IF NOT EXISTS idx_zona_tipo
                ON medicoes(zona, tipo_dado)";
            new SqliteCommand(sqlIdx, conn).ExecuteNonQuery();

            Console.WriteLine("[SERVIDOR] Base de dados inicializada.");
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

    static bool GuardarMedicao(string timestamp, string sensorId, string zona, string tipoDado, string valor)
    {
        mutexDB.WaitOne();
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            string sql = @"
                INSERT INTO medicoes (timestamp, sensor_id, zona, tipo_dado, valor)
                VALUES (@ts, @sid, @zona, @tipo, @val)";
            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ts", timestamp);
            cmd.Parameters.AddWithValue("@sid", sensorId);
            cmd.Parameters.AddWithValue("@zona", zona);
            cmd.Parameters.AddWithValue("@tipo", tipoDado);
            cmd.Parameters.AddWithValue("@val", valor);
            cmd.ExecuteNonQuery();
            Console.WriteLine($"[SERVIDOR] Guardado: {sensorId} | {zona} | {tipoDado} | {valor} | {timestamp}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro ao guardar medição: {ex.Message}");
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
                Console.WriteLine($"[SERVIDOR] [{enderecoGateway}] Recebido: {linha}");
                string[] partes = linha.Split('|');

                if (partes[0] == "DATA" && partes.Length == 6)
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
                        Console.WriteLine("[SERVIDOR] Mensagem DATA com campos vazios.");
                        writer.WriteLine("ERROR");
                        continue;
                    }

                    bool sucesso = GuardarMedicao(timestamp, sensorId, zona, tipoDado, valor);
                    writer.WriteLine(sucesso ? "ACK" : "ERROR");
                }
                else
                {
                    Console.WriteLine($"[SERVIDOR] Mensagem inválida de {enderecoGateway}: {linha}");
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
                TcpClient cliente = listener.AcceptTcpClient();
                Thread t = new Thread(TratarGateway);
                t.IsBackground = true;
                t.Start(cliente);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVIDOR] Erro ao aceitar ligação: {ex.Message}");
            }
        }
    }
}