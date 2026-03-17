using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;

class Servidor
{
    static Mutex mutexDB = new Mutex();
    static string dbPath = "medicoes.db";

    static void InicializarDB()
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        string sql = @"CREATE TABLE IF NOT EXISTS medicoes (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT NOT NULL,
            sensor_id TEXT NOT NULL,
            zona      TEXT NOT NULL,
            tipo_dado TEXT NOT NULL,
            valor     TEXT NOT NULL
        )";
        new SqliteCommand(sql, conn).ExecuteNonQuery();
        Console.WriteLine("[SERVIDOR] Base de dados inicializada.");
    }

    static void GuardarMedicao(string timestamp, string sensorId, string zona, string tipoDado, string valor)
    {
        mutexDB.WaitOne();
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            string sql = "INSERT INTO medicoes (timestamp, sensor_id, zona, tipo_dado, valor) VALUES (@ts, @sid, @zona, @tipo, @val)";
            var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ts", timestamp);
            cmd.Parameters.AddWithValue("@sid", sensorId);
            cmd.Parameters.AddWithValue("@zona", zona);
            cmd.Parameters.AddWithValue("@tipo", tipoDado);
            cmd.Parameters.AddWithValue("@val", valor);
            cmd.ExecuteNonQuery();
            Console.WriteLine($"[SERVIDOR] Guardado na DB: {sensorId} | {zona} | {tipoDado} | {valor}");
        }
        finally { mutexDB.ReleaseMutex(); }
    }

    static void TratarGateway(object obj)
    {
        TcpClient cliente = (TcpClient)obj;
        Console.WriteLine("[SERVIDOR] Gateway conectado em nova thread.");

        NetworkStream stream = cliente.GetStream();
        StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        string linha;
        while ((linha = reader.ReadLine()) != null)
        {
            Console.WriteLine($"[SERVIDOR] Recebido: {linha}");
            string[] partes = linha.Split('|');

            if (partes[0] == "DATA" && partes.Length == 6)
            {
                GuardarMedicao(partes[5], partes[1], partes[2], partes[3], partes[4]);
                writer.WriteLine("ACK");
            }
            else
            {
                Console.WriteLine("[SERVIDOR] Mensagem inválida.");
                writer.WriteLine("ERROR");
            }
        }

        Console.WriteLine("[SERVIDOR] Gateway desconectado.");
        cliente.Close();
    }

    static void Main(string[] args)
    {
        InicializarDB();

        TcpListener listener = new TcpListener(IPAddress.Any, 6000);
        listener.Start();
        Console.WriteLine("[SERVIDOR] A escutar na porta 6000...");

        while (true)
        {
            TcpClient cliente = listener.AcceptTcpClient();
            Thread t = new Thread(TratarGateway);
            t.Start(cliente);
        }
    }
}