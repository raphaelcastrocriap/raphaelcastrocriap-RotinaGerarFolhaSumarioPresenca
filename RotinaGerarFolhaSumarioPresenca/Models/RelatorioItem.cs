namespace RotinaGerarFolhaSumarioPresenca.Models;

/// <summary>
/// Registo de uma ação / resultado no relatório final.
/// </summary>
public class RelatorioItem
{
    public string? RefAccao       { get; set; }
    public string? Descricao      { get; set; }
    public string? NomeFormador   { get; set; }
    public string? EmailFormador  { get; set; }
    public string? NumSessao      { get; set; }
    public string? DataSessao     { get; set; }
    public string  Status         { get; set; } = "OK";
    public string? Mensagem       { get; set; }
}
