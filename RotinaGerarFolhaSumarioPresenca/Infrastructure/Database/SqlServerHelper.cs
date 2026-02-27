using Microsoft.Data.SqlClient;

namespace RotinaGerarFolhaSumarioPresenca.Infrastructure.Database;

/// <summary>
/// Encapsula uma conexão SQL Server. Abre/fecha por operação — sem estado persistente.
/// </summary>
public class SqlServerHelper : IDisposable
{
    private readonly string _connectionString;
    private SqlConnection?  _connection;

    public SqlServerHelper(string connectionString)
    {
        _connectionString = connectionString;
    }

    // ── Abre uma conexão reutilizável dentro do escopo desta instância ──────
    public SqlConnection GetOpenConnection()
    {
        if (_connection is null || _connection.State == System.Data.ConnectionState.Closed)
        {
            _connection?.Dispose();
            _connection = new SqlConnection(_connectionString);
            _connection.Open();
        }
        return _connection;
    }

    // ── Executa um SELECT e retorna DataTable ────────────────────────────────
    public System.Data.DataTable ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn    = new SqlConnection(_connectionString);
        using var cmd     = new SqlCommand(sql, conn);
        using var adapter = new SqlDataAdapter(cmd);

        AddParameters(cmd, parameters);

        conn.Open();
        var dt = new System.Data.DataTable();
        adapter.Fill(dt);
        return dt;
    }

    // ── Executa um comando DML (INSERT / UPDATE / DELETE) ────────────────────
    public int ExecuteNonQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn = new SqlConnection(_connectionString);
        using var cmd  = new SqlCommand(sql, conn);

        AddParameters(cmd, parameters);

        conn.Open();
        return cmd.ExecuteNonQuery();
    }

    // ── Testa se a conexão está funcional ────────────────────────────────────
    public bool TestConnection()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            return true;
        }
        catch { return false; }
    }

    // ── Helper ───────────────────────────────────────────────────────────────
    private static void AddParameters(SqlCommand cmd, Dictionary<string, object?>? parameters)
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
