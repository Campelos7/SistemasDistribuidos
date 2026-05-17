using Common.Config;
using Common.Interfaces;
using Common.Models;
using Common.Models.Enums;
using Microsoft.Data.Sqlite;

namespace Common.DataAccess;

/// <summary>
/// Implementação Repository com SQLite para medições e resultados de análise.
/// </summary>
public class SqliteMedicaoRepository : IMedicaoRepository
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteMedicaoRepository(string? dbPath = null)
    {
        _connectionString = $"Data Source={dbPath ?? AppSettings.DbPath}";
        InicializarSchema();
    }

    /// <summary>
    /// Cria as tabelas se ainda não existirem.
    /// </summary>
    private void InicializarSchema()
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string sqlMedicoes = """
                CREATE TABLE IF NOT EXISTS medicoes (
                    id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    sensor_id TEXT NOT NULL,
                    zona      TEXT NOT NULL,
                    tipo_dado TEXT NOT NULL,
                    valor     REAL NOT NULL
                );
                """;

            string sqlAnalises = """
                CREATE TABLE IF NOT EXISTS analises (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    tipo_analise    TEXT NOT NULL,
                    parametros_json TEXT NOT NULL,
                    resultado_json  TEXT NOT NULL,
                    executada_em    TEXT NOT NULL
                );
                """;

            new SqliteCommand(sqlMedicoes, conn).ExecuteNonQuery();
            new SqliteCommand(sqlAnalises, conn).ExecuteNonQuery();
        }
    }

    /// <inheritdoc />
    public void Guardar(Medicao medicao)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(
                "INSERT INTO medicoes (timestamp, sensor_id, zona, tipo_dado, valor) VALUES (@ts, @sid, @zona, @tipo, @val)",
                conn);
            cmd.Parameters.AddWithValue("@ts", medicao.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"));
            cmd.Parameters.AddWithValue("@sid", medicao.SensorId);
            cmd.Parameters.AddWithValue("@zona", medicao.Zona);
            cmd.Parameters.AddWithValue("@tipo", medicao.TipoDado);
            cmd.Parameters.AddWithValue("@val", medicao.Valor);
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc />
    public IEnumerable<Medicao> ObterTodas(
        string? sensorId = null,
        string? tipoDado = null,
        string? zona = null,
        DateTime? desde = null,
        DateTime? ate = null)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var sql = "SELECT timestamp, sensor_id, zona, tipo_dado, valor FROM medicoes WHERE 1=1";
            var cmd = new SqliteCommand { Connection = conn };

            if (!string.IsNullOrWhiteSpace(sensorId))
            {
                sql += " AND sensor_id = @sid";
                cmd.Parameters.AddWithValue("@sid", sensorId);
            }
            if (!string.IsNullOrWhiteSpace(tipoDado))
            {
                sql += " AND tipo_dado = @tipo";
                cmd.Parameters.AddWithValue("@tipo", tipoDado);
            }
            if (!string.IsNullOrWhiteSpace(zona))
            {
                sql += " AND zona = @zona";
                cmd.Parameters.AddWithValue("@zona", zona);
            }
            if (desde.HasValue)
            {
                sql += " AND timestamp >= @desde";
                cmd.Parameters.AddWithValue("@desde", desde.Value.ToString("yyyy-MM-ddTHH:mm:ss"));
            }
            if (ate.HasValue)
            {
                sql += " AND timestamp <= @ate";
                cmd.Parameters.AddWithValue("@ate", ate.Value.ToString("yyyy-MM-ddTHH:mm:ss"));
            }

            sql += " ORDER BY timestamp DESC";
            cmd.CommandText = sql;

            var lista = new List<Medicao>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime ts = DateTime.Parse(reader.GetString(0));
                lista.Add(new Medicao(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetDouble(4),
                    ts));
            }
            return lista;
        }
    }

    /// <inheritdoc />
    public void GuardarAnalise(AnaliseResultado resultado)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = new SqliteCommand(
                "INSERT INTO analises (tipo_analise, parametros_json, resultado_json, executada_em) VALUES (@tipo, @param, @res, @em)",
                conn);
            cmd.Parameters.AddWithValue("@tipo", resultado.TipoAnalise.ToString());
            cmd.Parameters.AddWithValue("@param", resultado.ParametrosJson);
            cmd.Parameters.AddWithValue("@res", resultado.ResultadoJson);
            cmd.Parameters.AddWithValue("@em", resultado.ExecutadaEm.ToString("yyyy-MM-ddTHH:mm:ss"));
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc />
    public IEnumerable<AnaliseResultado> ObterAnalises(TipoAnalise? tipo = null, int limite = 50)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string sql = "SELECT id, tipo_analise, parametros_json, resultado_json, executada_em FROM analises";
            if (tipo.HasValue)
                sql += " WHERE tipo_analise = @tipo";
            sql += " ORDER BY executada_em DESC LIMIT @lim";

            var cmd = new SqliteCommand(sql, conn);
            if (tipo.HasValue)
                cmd.Parameters.AddWithValue("@tipo", tipo.Value.ToString());
            cmd.Parameters.AddWithValue("@lim", limite);

            var lista = new List<AnaliseResultado>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var tipoEnum = Enum.Parse<TipoAnalise>(reader.GetString(1), ignoreCase: true);
                var resultado = new AnaliseResultado(
                    tipoEnum,
                    reader.GetString(2),
                    reader.GetString(3),
                    DateTime.Parse(reader.GetString(4)));
                resultado.Id = reader.GetInt32(0);
                lista.Add(resultado);
            }
            return lista;
        }
    }
}
