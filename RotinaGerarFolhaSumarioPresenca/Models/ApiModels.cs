using System.Text.Json.Serialization;

namespace RotinaGerarFolhaSumarioPresenca.Models;

// ─── Request ─────────────────────────────────────────────────────────────────
public class GerarFolhaApiRequest
{
    [JsonPropertyName("refAcao")]
    public string RefAcao { get; set; } = string.Empty;

    [JsonPropertyName("rowIdsSessoes")]
    public List<int> RowIdsSessoes { get; set; } = new();
}

// ─── Response ────────────────────────────────────────────────────────────────
public class GerarFolhaApiResponse
{
    [JsonPropertyName("ambiente")]
    public string? Ambiente { get; set; }

    [JsonPropertyName("sucesso")]
    public bool Sucesso { get; set; }

    [JsonPropertyName("mensagem")]
    public string? Mensagem { get; set; }

    [JsonPropertyName("totalProcessado")]
    public int TotalProcessado { get; set; }

    [JsonPropertyName("totalSucesso")]
    public int TotalSucesso { get; set; }

    [JsonPropertyName("totalFalhas")]
    public int TotalFalhas { get; set; }

    [JsonPropertyName("sessoes")]
    public List<GerarFolhaSessaoResult> Sessoes { get; set; } = new();
}

public class GerarFolhaSessaoResult
{
    [JsonPropertyName("rowIdSessao")]
    public int RowIdSessao { get; set; }

    [JsonPropertyName("numeroSessao")]
    public string? NumeroSessao { get; set; }

    [JsonPropertyName("dataSessao")]
    public string? DataSessao { get; set; }

    [JsonPropertyName("pathDocx")]
    public string? PathDocx { get; set; }

    [JsonPropertyName("pathPdf")]
    public string? PathPdf { get; set; }

    [JsonPropertyName("sucesso")]
    public bool Sucesso { get; set; }

    [JsonPropertyName("mensagemErro")]
    public string? MensagemErro { get; set; }
}
