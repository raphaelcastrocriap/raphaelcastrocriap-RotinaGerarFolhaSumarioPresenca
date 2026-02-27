using MySqlConnector;
using System.Data;

namespace RotinaGerarFolhaSumarioPresenca.Infrastructure.Database;

/// <summary>
/// Encapsula uma conexão MySQL. Abre/fecha por operação — sem estado persistente.
/// Utilizado para as bases Moodle e CRM (únicas bases MySQL da empresa).
/// </summary>
public class MySqlHelper : IDisposable
{
    private readonly string       _connectionString;
    private MySqlConnection?      _connection;

    public MySqlHelper(string connectionString)
    {
        _connectionString = connectionString;
    }

    // ── Abre uma conexão reutilizável dentro do escopo desta instância ──────
    public MySqlConnection GetOpenConnection()
    {
        if (_connection is null || _connection.State == ConnectionState.Closed)
        {
            _connection?.Dispose();
            _connection = new MySqlConnection(_connectionString);
            _connection.Open();
        }
        return _connection;
    }

    // ── Executa um SELECT e retorna DataTable ────────────────────────────────
    public DataTable ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn    = new MySqlConnection(_connectionString);
        using var cmd     = new MySqlCommand(sql, conn);
        using var adapter = new MySqlDataAdapter(cmd);

        AddParameters(cmd, parameters);

        conn.Open();
        var dt = new DataTable();
        adapter.Fill(dt);
        return dt;
    }

    // ── Executa um comando DML (INSERT / UPDATE / DELETE) ────────────────────
    public int ExecuteNonQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn = new MySqlConnection(_connectionString);
        using var cmd  = new MySqlCommand(sql, conn);

        AddParameters(cmd, parameters);

        conn.Open();
        return cmd.ExecuteNonQuery();
    }

    // ── Testa se a conexão está funcional ────────────────────────────────────
    public bool TestConnection()
    {
        try
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return true;
        }
        catch { return false; }
    }

    // ── Helper ───────────────────────────────────────────────────────────────
    private static void AddParameters(MySqlCommand cmd, Dictionary<string, object?>? parameters)
    {
        if (parameters is null) return;
        foreach (var kv in parameters)
            cmd.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
