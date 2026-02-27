using RotinaGerarFolhaSumarioPresenca.Infrastructure.Database;
using RotinaGerarFolhaSumarioPresenca.Infrastructure.Logging;

namespace RotinaGerarFolhaSumarioPresenca.Services;

/// <summary>
/// Grava logs de ações realizadas na tabela sv_logs da SecretariaVirtual.
/// Erros de gravação são apenas logados localmente — nunca param a rotina.
/// </summary>
public class LogDbService
{
    private readonly SqlServerHelper _db;
    private readonly AppLogger       _logger;

    public LogDbService(string connectionString, AppLogger logger)
    {
        _db     = new SqlServerHelper(connectionString);
        _logger = logger;
    }

    /// <summary>
    /// Registra uma ação realizada na tabela sv_logs.
    /// Falhas são silenciosas (apenas log local) — nunca param a rotina.
    /// </summary>
    public void RegistrarAcaoLog(
        string idEntidade,
        string mensagem,
        string menu,
        string refAccao)
    {
        try
        {
            // Sanitiza para SQL inline (parametrizado abaixo via dicionário)
            string sql = @"
                INSERT INTO sv_logs (idFormando, refAcao, dataregisto, registo, menu, username)
                VALUES (@idFormando, @refAcao, GETDATE(), @registo, @menu, 'system_rotina')";

            _db.ExecuteNonQuery(sql, new Dictionary<string, object?>
            {
                { "@idFormando", idEntidade                          },
                { "@refAcao",    refAccao                            },
                { "@registo",    mensagem                            },
                { "@menu",       menu                                }
            });
        }
        catch (Exception ex)
        {
            // Log de BD nunca para a rotina
            _logger.Aviso($"[LogDb] Falha ao gravar log '{menu}' para entidade {idEntidade}: {ex.Message}");
        }
    }

    /// <summary>
    /// Registra múltiplas ações na tabela sv_logs (itera internamente sobre RegistrarAcaoLog).
    /// </summary>
    public void RegistrarAcoesLog(IEnumerable<(string idEntidade, string mensagem, string menu, string refAccao)> acoes)
    {
        foreach (var (idEntidade, mensagem, menu, refAccao) in acoes)
            RegistrarAcaoLog(idEntidade, mensagem, menu, refAccao);
    }
}
