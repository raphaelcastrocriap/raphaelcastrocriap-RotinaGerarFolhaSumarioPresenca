namespace RotinaGerarFolhaSumarioPresenca.Models;

// ─── Configurações SMTP (único bloco de Email no appsettings) ───────────────────
public class EmailSettings
{
    public string Remetente     { get; set; } = string.Empty;
    public string NomeRemetente { get; set; } = "Instituto CRIAP";
    public string Password      { get; set; } = string.Empty;
    public string SmtpHost      { get; set; } = string.Empty;
    public int    SmtpPort      { get; set; } = 25;
}

// ─── Chaves de conexão disponíveis (nomes das entradas em ConnectionStrings) ──────────
public static class DbKey
{
    public const string HT                  = "DatabaseHT";
    public const string SV                  = "DatabaseSV";
    public const string Moodle              = "DatabaseMoodle";
    public const string MoodleNew           = "DatabaseMoodleNew";
    public const string PortalAluno         = "DatabasePortalAluno";
    public const string PortalDoProfessor   = "DatabasePortalDoProfessor";
    public const string CRM                 = "DatabaseCRM";
    public const string DtpDigital          = "DatabaseDtpDigital";
    public const string HT_Test             = "DatabaseHT_Test";
    public const string SV_Test             = "DatabaseSV_Test";
}

// ─── Configurações de API ────────────────────────────────────────────────────
public class ApiSettings
{
    public string BaseUrl      { get; set; } = string.Empty;
    public string BaseUrlTeste { get; set; } = "http://localhost:5141";
    public int    TimeoutSeconds { get; set; } = 60;

    /// <summary>Retorna a URL correta conforme o modo de execução.</summary>
    public string GetUrl(bool modoTeste) => modoTeste ? BaseUrlTeste : BaseUrl;
}

// ─── Configurações Globais (infraestrutura apenas) ─────────────────────────
// ModoTeste, DataFiltroOverride e emails de destino ficam em cada rotina.
public class AppSettings
{
    public EmailSettings              Email             { get; set; } = new();
    public Dictionary<string, string> ConnectionStrings { get; set; } = new();
    public ApiSettings                Api               { get; set; } = new();

    /// <summary>
    /// Retorna a connection string pelo nome (DbKey.*). Lanza InvalidOperationException se não encontrada.
    /// </summary>
    public string GetConnectionString(string key)
    {
        if (ConnectionStrings.TryGetValue(key, out var cs) && !string.IsNullOrWhiteSpace(cs))
            return cs;
        throw new InvalidOperationException($"Connection string '{key}' não encontrada no appsettings.json");
    }
}
