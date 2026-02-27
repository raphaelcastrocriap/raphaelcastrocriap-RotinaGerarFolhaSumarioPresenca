using System.Net;
using System.Text;

namespace RotinaGerarFolhaSumarioPresenca.Infrastructure.Logging;

/// <summary>
/// Logger da aplicação.
/// — Escreve no Console com timestamp e nível.
/// — Mantém buffer em memória para inclusão no relatório final.
/// — Erros críticos são também reportados por email (via callback).
/// </summary>
public class AppLogger
{
    private readonly string              _nomeRotina;
    private readonly List<LogEntry>      _buffer = new();
    private readonly Action<string>?     _onErroCallback; // chamado com mensagem ao registrar erro crítico

    public IReadOnlyList<LogEntry> Entries => _buffer;

    public AppLogger(string nomeRotina, Action<string>? onErroCallback = null)
    {
        _nomeRotina      = nomeRotina;
        _onErroCallback  = onErroCallback;
    }

    // ── Níveis ───────────────────────────────────────────────────────────────
    public void Info(string mensagem)    => Registar(NivelLog.INFO,  mensagem);
    public void Sucesso(string mensagem) => Registar(NivelLog.OK,    mensagem);
    public void Aviso(string mensagem)   => Registar(NivelLog.AVISO, mensagem);
    public void Erro(string mensagem, Exception? ex = null)
    {
        string completo = ex is null ? mensagem : $"{mensagem} | {ex}";
        Registar(NivelLog.ERRO, completo);
        _onErroCallback?.Invoke(completo);
    }

    // ── Buffer HTML para relatório ───────────────────────────────────────────
    public string GerarHtmlLog()
    {
        if (_buffer.Count == 0)
            return "<p>Nenhum log registado.</p>";

        var sb = new StringBuilder();
        sb.AppendLine("<table border='0' cellpadding='4' cellspacing='0' style='border-collapse:collapse;font-size:12px;width:100%;'>");
        sb.AppendLine("  <tr style='background:#ed7520;color:#fff;'><th>Hora</th><th>N&iacute;vel</th><th>Mensagem</th></tr>");

        foreach (var e in _buffer)
        {
            string bg = e.Nivel switch
            {
                NivelLog.ERRO  => "#ffe4d6",
                NivelLog.AVISO => "#fff9e6",
                NivelLog.OK    => "#eafaf1",
                _              => "#fff"
            };
            string cor = e.Nivel switch
            {
                NivelLog.ERRO  => "#c0392b",
                NivelLog.AVISO => "#d68910",
                NivelLog.OK    => "#27ae60",
                _              => "#333"
            };
            sb.AppendLine($"<tr style='background:{bg};'>"
                        + $"<td style='white-space:nowrap;'>{e.Hora:HH:mm:ss}</td>"
                        + $"<td style='color:{cor};font-weight:bold;white-space:nowrap;'>{e.Nivel}</td>"
                        + $"<td>{WebUtility.HtmlEncode(e.Mensagem)}</td>"
                        + "</tr>");
        }

        sb.AppendLine("</table>");
        return sb.ToString();
    }

    // ── Interno ──────────────────────────────────────────────────────────────
    private void Registar(NivelLog nivel, string mensagem)
    {
        var entry = new LogEntry(nivel, mensagem);
        _buffer.Add(entry);

        // Saída no console com cor
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = nivel switch
        {
            NivelLog.ERRO  => ConsoleColor.Red,
            NivelLog.AVISO => ConsoleColor.Yellow,
            NivelLog.OK    => ConsoleColor.Green,
            _              => ConsoleColor.White
        };
        Console.WriteLine($"[{entry.Hora:HH:mm:ss}] [{nivel,-5}] [{_nomeRotina}] {mensagem}");
        Console.ForegroundColor = originalColor;
    }
}

public enum NivelLog { INFO, OK, AVISO, ERRO }

public record LogEntry(NivelLog Nivel, string Mensagem, DateTime Hora = default)
{
    public DateTime Hora { get; init; } = Hora == default ? DateTime.Now : Hora;
}
