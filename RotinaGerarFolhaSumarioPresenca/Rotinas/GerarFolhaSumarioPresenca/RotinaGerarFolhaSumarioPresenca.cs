using System.Text;
using System.Text.Json;
using RotinaGerarFolhaSumarioPresenca.Infrastructure.Database;
using RotinaGerarFolhaSumarioPresenca.Infrastructure.Email;
using RotinaGerarFolhaSumarioPresenca.Infrastructure.Logging;
using RotinaGerarFolhaSumarioPresenca.Models;
using RotinaGerarFolhaSumarioPresenca.Services;

namespace RotinaGerarFolhaSumarioPresenca.Rotinas.GerarFolhaSumarioPresenca;

/// <summary>
/// Rotina: Gerar Folha Sumário de Presença (F029)
/// ─────────────────────────────────────────────
/// 1. Consulta sessões presenciais do dia no HT.
/// 2. Agrupa por RefAccao e chama a API de geração do F029.
/// 3. Para cada sessão gerada com sucesso, envia email ao formador com o PDF em anexo.
/// 4. Grava log das ações na BD.
/// 5. Envia email de relatório final para Informática + Pedagógico.
///
/// REGRA: a rotina nunca termina com exceção não tratada.
///        Erros são registados e incluídos no relatório.
/// </summary>
public class GerarFolhaPresencaRotina
{
    // ── Identificação ─────────────────────────────────────────────────────────
    private const string NOME_ROTINA  = "RotinaGerarFolhaSumarioPresenca";
    private const string VERSAO       = "1.0.0";

    // ── Endpoint da API ───────────────────────────────────────────────────────
    private const string API_ENDPOINT = "/api/v2/acoes-dtp/gerar-f029-preenchido";

    // ══════════════════════════════════════════════════════════════════════════
    //  CONSTANTES DE DESENVOLVIMENTO
    //  ► Únicas variáveis que devem ser alteradas para testes/desenvolvimento.
    //    O appsettings.json contém apenas infraestrutura.
    //    Sempre que ModoTeste estar true, os emails são redirecionados para EmailModoTeste e API SV aponta para localhost.
    // ══════════════════════════════════════════════════════════════════════════
    private const bool   ModoTeste          = false;          // true → emails só para EmailTeste + API local
    private const string DataFiltroOverride = "";             // "" = ontem (dia anterior) | "2026-01-10" = data fixa para teste
    private const string EmailInformatica   = "informatica@criap.com";
    private const string EmailPedagogico    = "tecnicopedagogico@criap.com";
    private const string EmailModoTeste     = "raphaelcastro@criap.com";

    // ── Formadores internos/coordenadores que não recebem email (ex: pessoal admin) ───
    private static readonly int[] FormadoresExcluidos = { 699, 704, 827, 1046, 1053, 15683, 15684, 1425, 16221 };

    // ── Dependências ──────────────────────────────────────────────────────────
    private readonly AppSettings       _cfg;
    private readonly AppLogger         _logger;
    private readonly EmailService      _email;
    private readonly SqlServerHelper   _dbHt;
    private readonly LogDbService      _logDb;
    private readonly HttpClient        _http;

    // ── Estado da execução ────────────────────────────────────────────────────
    private readonly List<RelatorioItem> _relatorio = new();

    // ─────────────────────────────────────────────────────────────────────────
    public GerarFolhaPresencaRotina(AppSettings cfg)
    {
        _cfg    = cfg;
        _logger = new AppLogger(NOME_ROTINA, onErroCallback: EnviarEmailErroInterno);
        _email  = new EmailService(cfg.Email, ModoTeste, EmailModoTeste);
        _dbHt   = new SqlServerHelper(cfg.GetConnectionString(DbKey.HT));
        _logDb  = new LogDbService(cfg.GetConnectionString(DbKey.SV), _logger);
        _http   = new HttpClient
        {
            BaseAddress = new Uri(cfg.Api.GetUrl(ModoTeste)),
            Timeout     = TimeSpan.FromSeconds(cfg.Api.TimeoutSeconds)
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PONTO DE ENTRADA
    // ══════════════════════════════════════════════════════════════════════════
    public async Task ExecutarAsync()
    {
        _logger.Info($"=== {NOME_ROTINA} v{VERSAO} iniciado ===");
        _logger.Info($"Modo Teste: {ModoTeste} | Data alvo override: '{DataFiltroOverride}'");

        // ── Resolução da data alvo 
        DateTime dataAlvo = ResolverDataAlvo();
        _logger.Info($"Data alvo: {dataAlvo:dd/MM/yyyy}");

        // ── 1. Consultar sessões 
        List<SessaoFormador> sessoes = ConsultarSessoes(dataAlvo);

        if (sessoes.Count == 0)
        {
            _logger.Aviso($"Nenhuma sessão presencial encontrada para {dataAlvo:dd/MM/yyyy}.");
            await EnviarRelatorioFinalAsync(dataAlvo);
            return;
        }

        _logger.Info($"{sessoes.Count} registo(s) encontrado(s).");

        // ── 2. Agrupar por RefAccao 
        var grupos = sessoes.GroupBy(s => s.RefAccao ?? "SEM_REF");

        foreach (var grupo in grupos)
        {
            string    refAccao = grupo.Key;
            List<int> rowIds   = grupo.Select(s => s.VersaoRowid).Distinct().ToList();

            _logger.Info($"→ Processando RefAccao '{refAccao}' com {rowIds.Count} sessão(ões)...");

            // ── 3. Chamar a API 
            GerarFolhaApiResponse? apiResp = await ChamarApiAsync(refAccao, rowIds);

            if (apiResp is null || !apiResp.Sucesso)
            {
                string errMsg = apiResp?.Mensagem ?? "Sem resposta / timeout da API";
                _logger.Erro($"API falhou para '{refAccao}': {errMsg}");
                _relatorio.Add(new RelatorioItem
                {
                    RefAccao  = refAccao,
                    Status    = "ERRO_API",
                    Mensagem  = errMsg
                });
                continue;
            }

            _logger.Sucesso($"  API OK: {apiResp.TotalSucesso} gerada(s), {apiResp.TotalFalhas} falha(s).");

            // ── 4. Registar falhas de geração 
            foreach (var sessaoErro in apiResp.Sessoes.Where(s => !s.Sucesso))
            {
                _logger.Erro($"  Falha ao gerar sessão {sessaoErro.NumeroSessao}: {sessaoErro.MensagemErro}");
                _relatorio.Add(new RelatorioItem
                {
                    RefAccao   = refAccao,
                    NumSessao  = sessaoErro.NumeroSessao,
                    DataSessao = sessaoErro.DataSessao,
                    Status     = "ERRO_GERACAO",
                    Mensagem   = sessaoErro.MensagemErro ?? "Falha ao gerar F029"
                });
            }

            // ── 5. Processar sessões geradas com sucesso 
            foreach (var sessaoOk in apiResp.Sessoes.Where(s => s.Sucesso))
            {
                // Todos os formadores desta sessão (pode haver mais de um)
                var formadores = sessoes
                    .Where(s => s.VersaoRowid == sessaoOk.RowIdSessao)
                    .ToList();

                foreach (var formador in formadores)
                {
                    // ── 5a. Enviar email ao formador 
                    EnviarEmailFormador(formador, sessaoOk);

                    // ── 5b. Gravar log na BD 
                    _logDb.RegistrarAcaoLog(
                        idEntidade : formador.CodigoFormador.ToString(),
                        mensagem   : $"F029 gerado e email enviado | Sessão {sessaoOk.NumeroSessao} | {sessaoOk.DataSessao} | PDF: {sessaoOk.PathPdf}",
                        menu       : "Folha Sumário Presença - F029",
                        refAccao   : refAccao
                    );
                }
            }
        }

        // ── 6. Relatório final
        await EnviarRelatorioFinalAsync(dataAlvo);
        _logger.Info($"=== {NOME_ROTINA} finalizado ===");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CONSULTA SQL — sessões presenciais do dia
    // ══════════════════════════════════════════════════════════════════════════
    private List<SessaoFormador> ConsultarSessoes(DateTime dataAlvo)
    {
        _logger.Info("Consultando sessões na base de dados HT...");

        string excluidos = string.Join(",", FormadoresExcluidos);

        // @dataAlvo é passado com parâmetro para evitar SQL injection
        string sql = $@"
            SELECT DISTINCT
                s.versao_rowid,
                s.Data,
                s.Hora_Inicio,
                s.Hora_Fim,
                s.Rowid_Modulo,
                s.Num_Sessao,
                f.Nome_Abreviado,
                cu.Descricao,
                a.Numero_Accao,
                a.Ref_Accao,
                f.Codigo_Formador,
                COALESCE(c.Email1, c.Email2) AS Email
            FROM TBForSessoesFormadores sf
            INNER JOIN TBForSessoes s   ON s.versao_rowid = sf.rowid_sessao
            INNER JOIN TBForAccoes a    ON s.Rowid_Accao  = a.versao_rowid
            INNER JOIN TBForFormadores f ON f.Codigo_Formador = sf.codigo_formador
            INNER JOIN TBGerContactos c  ON f.versao_rowid = c.Codigo_Entidade
                                        AND c.Tipo_Entidade = 4
            INNER JOIN TBForCursos cu   ON cu.Codigo_Curso = a.Codigo_Curso
            WHERE CAST(s.Data AS DATE) = @dataAlvo
              AND s.Comp_elr = 'P'
              AND f.Codigo_Formador NOT IN ({excluidos})
              AND COALESCE(c.Email1, c.Email2) IS NOT NULL";

        try
        {
            var dt     = _dbHt.ExecuteQuery(sql, new Dictionary<string, object?> { { "@dataAlvo", dataAlvo.Date } });
            var result = new List<SessaoFormador>(dt.Rows.Count);

            foreach (System.Data.DataRow row in dt.Rows)
            {
                result.Add(new SessaoFormador
                {
                    VersaoRowid    = Convert.ToInt32(row["versao_rowid"]),
                    Data           = row.IsNull("Data")           ? null : (DateTime?)row["Data"],
                    HoraInicio     = row.IsNull("Hora_Inicio")    ? null : row["Hora_Inicio"].ToString(),
                    HoraFim        = row.IsNull("Hora_Fim")       ? null : row["Hora_Fim"].ToString(),
                    RowidModulo    = row.IsNull("Rowid_Modulo")   ? null : (int?)Convert.ToInt32(row["Rowid_Modulo"]),
                    NumSessao      = row.IsNull("Num_Sessao")     ? null : row["Num_Sessao"].ToString(),
                    NomeAbreviado  = row.IsNull("Nome_Abreviado") ? null : row["Nome_Abreviado"].ToString()!.Trim(),
                    Descricao      = row.IsNull("Descricao")      ? null : row["Descricao"].ToString()!.Trim(),
                    NumeroAccao    = row.IsNull("Numero_Accao")   ? 0    : Convert.ToInt32(row["Numero_Accao"]),
                    RefAccao       = row.IsNull("Ref_Accao")      ? null : row["Ref_Accao"].ToString()!.Trim(),
                    CodigoFormador = row.IsNull("Codigo_Formador")? 0    : Convert.ToInt32(row["Codigo_Formador"]),
                    Email          = row.IsNull("Email")          ? null : row["Email"].ToString()!.Trim()
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Erro("Erro ao consultar sessões na BD", ex);
            return new List<SessaoFormador>();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CHAMADA À API
    // ══════════════════════════════════════════════════════════════════════════
    private async Task<GerarFolhaApiResponse?> ChamarApiAsync(string refAccao, List<int> rowIds)
    {
        try
        {
            var body = new GerarFolhaApiRequest
            {
                RefAcao       = refAccao,
                RowIdsSessoes = rowIds
            };

            string json    = JsonSerializer.Serialize(body);
            var    content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Info($"  POST {API_ENDPOINT} | RefAccao={refAccao} | {rowIds.Count} sessão(ões)");

            HttpResponseMessage httpResp = await _http.PostAsync(API_ENDPOINT, content);
            string              respBody = await httpResp.Content.ReadAsStringAsync();

            if (httpResp.IsSuccessStatusCode)
            {
                var apiResp = JsonSerializer.Deserialize<GerarFolhaApiResponse>(respBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return apiResp;
            }

            return new GerarFolhaApiResponse
            {
                Sucesso  = false,
                Mensagem = $"HTTP {(int)httpResp.StatusCode}: {respBody}"
            };
        }
        catch (TaskCanceledException)
        {
            return new GerarFolhaApiResponse
            {
                Sucesso  = false,
                Mensagem = $"Timeout ao chamar a API (>{_cfg.Api.TimeoutSeconds}s)"
            };
        }
        catch (Exception ex)
        {
            return new GerarFolhaApiResponse
            {
                Sucesso  = false,
                Mensagem = $"Exceção ao chamar API: {ex.Message}"
            };
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  EMAIL PARA O FORMADOR
    // ══════════════════════════════════════════════════════════════════════════
    private void EnviarEmailFormador(SessaoFormador formador, GerarFolhaSessaoResult sessaoResult)
    {
        string dataFormatada = formador.Data.HasValue
            ? formador.Data.Value.ToString("dd/MM/yyyy")
            : (sessaoResult.DataSessao ?? DateTime.Now.ToString("dd/MM/yyyy"));

        string dataFormatadaPonto = dataFormatada.Replace("/", ".");
        string horaInicio         = FormatarHora(formador.HoraInicio);
        string horaFim            = FormatarHora(formador.HoraFim);

        string assunto = $"Instituto CRIAP || Folha Sumário Presença - {formador.Descricao} - {dataFormatada}";

        var corpo = new StringBuilder();
        corpo.AppendLine($"<p>Estimado(a) Professor(a) <b>{formador.NomeAbreviado}</b>,</p>");
        corpo.AppendLine("<p>Fazemos votos de que se encontre bem.</p>");
        corpo.AppendLine($"<p>No seguimento da aula prevista para o dia <b>{dataFormatadaPonto}</b>, a decorrer no hor&aacute;rio das <b>{horaInicio}</b> &agrave;s <b>{horaFim}</b>, procedemos ao envio, em anexo, da folha de presen&ccedil;as preenchida.</p>");
        corpo.AppendLine("<p>Caso necessite de qualquer esclarecimento adicional, n&atilde;o hesite em contactar-nos.</p>");
        corpo.AppendLine("<p>Com os melhores cumprimentos,<br><b>Departamento T&eacute;cnico-Pedag&oacute;gico</b><br>Instituto CRIAP</p>");

        // Anexo — apenas PDF
        var anexos = new List<string>();
        if (!string.IsNullOrWhiteSpace(sessaoResult.PathPdf))
            anexos.Add(sessaoResult.PathPdf);

        string? erro = _email.EnviarEmail(
            destinatarios    : (ModoTeste) ? new[] { EmailModoTeste } : new[] { formador.Email! },
            assunto          : assunto,
            corpoHtml        : _email.ConstruirLayoutHtml("Folha de Presen&ccedil;as (F029)", corpo.ToString(), rodapeInterno: false),
            cc               : (ModoTeste) ? new[] { EmailModoTeste } : new[] { EmailPedagogico, EmailInformatica },
            replyTo          : (ModoTeste) ? new[] { EmailModoTeste } : new[] { EmailPedagogico },
            anexosCaminhos   : anexos
        );

        if (erro is null)
        {
            _logger.Sucesso($"  Email enviado → {formador.NomeAbreviado} ({formador.Email}) | Sessão {sessaoResult.NumeroSessao}");
            _relatorio.Add(new RelatorioItem
            {
                RefAccao      = formador.RefAccao,
                Descricao     = formador.Descricao,
                NomeFormador  = formador.NomeAbreviado,
                EmailFormador = formador.Email,
                NumSessao     = sessaoResult.NumeroSessao,
                DataSessao    = $"{dataFormatada} {horaInicio}-{horaFim}",
                Status        = "OK",
                Mensagem      = "Email enviado com sucesso"
            });
        }
        else
        {
            _logger.Erro($"  Falha ao enviar email para {formador.NomeAbreviado}: {erro}");
            _relatorio.Add(new RelatorioItem
            {
                RefAccao      = formador.RefAccao,
                Descricao     = formador.Descricao,
                NomeFormador  = formador.NomeAbreviado,
                EmailFormador = formador.Email,
                NumSessao     = sessaoResult.NumeroSessao,
                DataSessao    = $"{dataFormatada} {horaInicio}-{horaFim}",
                Status        = "ERRO_EMAIL",
                Mensagem      = erro
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  EMAIL DE RELATÓRIO FINAL
    // ══════════════════════════════════════════════════════════════════════════
    private async Task EnviarRelatorioFinalAsync(DateTime dataAlvo)
    {
        _logger.Info("Preparando e enviando email de relatório final...");

        try
        {
            int okCount   = _relatorio.Count(r => r.Status == "OK");
            int errCount  = _relatorio.Count(r => r.Status != "OK");
            string dataFmt = dataAlvo.ToString("dd/MM/yyyy");

            // ── Tabela de resultados
            string tabelaHtml = MontarTabelaRelatorio(okCount, errCount);

            // ── Log da execução
            string logHtml = _logger.GerarHtmlLog();

            // ── Corpo completo
            var corpo = new StringBuilder();
            corpo.AppendLine($"<p><b>Data alvo:</b> {dataFmt} &nbsp;&nbsp; <b>Modo Teste:</b> {ModoTeste}</p>");
            corpo.AppendLine(tabelaHtml);
            corpo.AppendLine("<hr style='margin:24px 0;border:none;border-top:1px solid #ddd;'>");
            corpo.AppendLine($"<h3 style='font-size:13px;color:#555;'>Log de Execu&ccedil;&atilde;o</h3>");
            corpo.AppendLine(logHtml);

            // Assembly.Location é vazio em publicações single-file; usa o caminho do processo.
            var exePath   = Environment.ProcessPath ?? typeof(GerarFolhaPresencaRotina).Assembly.Location;
            var buildDate = !string.IsNullOrEmpty(exePath)
                ? File.GetLastWriteTime(exePath).ToString("dd/MM/yyyy HH:mm")
                : "N/A";

            string html = _email.ConstruirLayoutHtml(
                titulo         : $"Relat&oacute;rio F029 &mdash; Sess&otilde;es de {dataFmt}",
                conteudo       : corpo.ToString(),
                rodapeInterno  : true,
                versao         : $"v{VERSAO} | {NOME_ROTINA} | Build: {buildDate}");

            
            _email.EnviarEmail(
                destinatarios : (ModoTeste) ? new[] { EmailModoTeste } : new[] { EmailInformatica, EmailPedagogico },
                assunto       : $"Instituto CRIAP || Relatório Folha Sumário Presença F029 - Sessões {dataFmt}",
                corpoHtml     : html
            );

            _logger.Sucesso("Email de relatório final enviado.");
        }
        catch (Exception ex)
        {
            _logger.Erro("Erro ao enviar relatório final", ex);
        }

        await Task.CompletedTask;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Callback do AppLogger para erros críticos — envia email para Informática automaticamente.
    /// Nunca lança exceção ao chamador.
    /// </summary>
    private void EnviarEmailErroInterno(string detalhe)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<p style='color:#c0392b;'><b>Ocorreu um erro na rotina <u>{NOME_ROTINA}</u>.</b></p>");
            sb.AppendLine("<pre style='background:#ffe4d6;border:1px solid #ed7520;padding:12px;font-size:11px;white-space:pre-wrap;word-break:break-all;'>");
            sb.AppendLine(System.Net.WebUtility.HtmlEncode(detalhe));
            sb.AppendLine("</pre>");
            _email?.EnviarEmail(
                destinatarios : (ModoTeste) ? new[] { EmailModoTeste } : new[] { EmailInformatica },
                assunto       : $"ERRO - {NOME_ROTINA} [{DateTime.Now:dd/MM/yyyy HH:mm}]",
                corpoHtml     : _email.ConstruirLayoutHtml($"Erro na Rotina &mdash; {NOME_ROTINA}", sb.ToString(), rodapeInterno: true));
        }
        catch { /* nunca pode parar a rotina */ }
    }

    /// <summary>Resolve a data alvo: DataFiltroOverride (para testes) ou o dia anterior (ontem).</summary>
    private DateTime ResolverDataAlvo()
    {
        if (!string.IsNullOrWhiteSpace(DataFiltroOverride)
            && DateTime.TryParse(DataFiltroOverride, out DateTime overrideDate))
        {
            _logger.Aviso($"DataFiltroOverride ativo: usando {overrideDate:dd/MM/yyyy} em vez de ontem.");
            return overrideDate.Date;
        }
        return DateTime.Today;
    }

    /// <summary>Converte "19:00:00", "19:00" ou datetime completo em "19h00".</summary>
    private static string FormatarHora(string? hora)
    {
        if (string.IsNullOrWhiteSpace(hora)) return "";
        if (DateTime.TryParse(hora, out DateTime dt))
            return dt.ToString("HH") + "h" + dt.ToString("mm");
        if (TimeSpan.TryParse(hora, out TimeSpan ts))
            return ts.Hours.ToString("D2") + "h" + ts.Minutes.ToString("D2");
        return hora;
    }

    /// <summary>Constrói a tabela HTML do relatório. Para quando não há itens, mostra mensagem.</summary>
    private string MontarTabelaRelatorio(int okCount, int errCount)
    {
        if (_relatorio.Count == 0)
            return "<p style='color:#888;'>Nenhum item processado.</p>";

        var linhas = new StringBuilder();
        foreach (var item in _relatorio)
        {
            string bg = item.Status == "OK" ? "#eafaf1" : "#ffe4d6";
            string cor = item.Status == "OK" ? "#27ae60" : "#c0392b";
            linhas.AppendLine($@"<tr style='background:{bg};'>
                <td>{item.RefAccao}</td>
                <td>{item.Descricao}</td>
                <td>{item.NomeFormador}</td>
                <td>{item.EmailFormador}</td>
                <td style='text-align:center;'>{item.NumSessao}</td>
                <td>{item.DataSessao}</td>
                <td style='color:{cor};font-weight:bold;text-align:center;'>{item.Status}</td>
                <td>{System.Net.WebUtility.HtmlEncode(item.Mensagem ?? "")}</td>
              </tr>");
        }

        return $@"
            <p style='margin:8px 0;'>
                <b>Total:</b> {_relatorio.Count}
                &nbsp;|&nbsp;<b style='color:#27ae60;'>Sucesso:</b> {okCount}
                &nbsp;|&nbsp;<b style='color:#c0392b;'>Erros:</b> {errCount}
            </p>
            <table border='0' cellpadding='5' cellspacing='0'
                   style='border-collapse:collapse;font-size:12px;width:100%;'>
              <tr style='background:#ed7520;color:#fff;'>
                <th>Ref A&ccedil;&atilde;o</th>
                <th>Curso</th>
                <th>Formador</th>
                <th>Email</th>
                <th>Sess&atilde;o N&ordm;</th>
                <th>Data/Hora</th>
                <th>Status</th>
                <th>Mensagem</th>
              </tr>
              {linhas}
            </table>";
    }
}
