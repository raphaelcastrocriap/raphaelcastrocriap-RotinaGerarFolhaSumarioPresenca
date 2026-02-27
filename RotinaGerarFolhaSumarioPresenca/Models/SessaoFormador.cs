namespace RotinaGerarFolhaSumarioPresenca.Models;

/// <summary>
/// Linha retornada pelo SQL — sessão + formador.
/// </summary>
public class SessaoFormador
{
    public int       VersaoRowid    { get; set; }
    public DateTime? Data           { get; set; }
    public string?   HoraInicio     { get; set; }
    public string?   HoraFim        { get; set; }
    public int?      RowidModulo    { get; set; }
    public string?   NumSessao      { get; set; }
    public string?   NomeAbreviado  { get; set; }
    public string?   Descricao      { get; set; }
    public int       NumeroAccao    { get; set; }
    public string?   RefAccao       { get; set; }
    public int       CodigoFormador { get; set; }
    public string?   Email          { get; set; }
}
