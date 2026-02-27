using System.Net;
using System.Net.Mail;
using System.Text;
using RotinaGerarFolhaSumarioPresenca.Models;

namespace RotinaGerarFolhaSumarioPresenca.Infrastructure.Email;

/// <summary>
/// Serviço de envio de emails via SMTP — responsável apenas pela mecânica de envio.
/// Quem recebe e o que enviar é responsabilidade de quem chama.
/// </summary>
public class EmailService
{
    private readonly EmailSettings _cfg;
    private readonly bool          _modoTeste;
    private readonly string        _emailTeste;

    /// <param name="emailTeste">Destino único de todos os emails quando modoTeste=true.</param>
    public EmailService(EmailSettings cfg, bool modoTeste, string emailTeste)
    {
        _cfg        = cfg;
        _modoTeste  = modoTeste;
        _emailTeste = emailTeste;
    }

    // ── Envio genérico ───────────────────────────────────────────────────────
    /// <summary>
    /// Envia um email HTML. Retorna null se OK, mensagem de erro se falhar.
    /// </summary>
    public string? EnviarEmail(
        IEnumerable<string> destinatarios,
        string assunto,
        string corpoHtml,
        IEnumerable<string>? cc          = null,
        IEnumerable<string>? replyTo     = null,
        IEnumerable<string>? anexosCaminhos = null)
    {
        try
        {
            using var client = CriarSmtpClient();
            using var mm     = new MailMessage();

            mm.From            = new MailAddress(_cfg.Remetente, _cfg.NomeRemetente, Encoding.UTF8);
            mm.Subject         = assunto;
            mm.BodyEncoding    = Encoding.UTF8;
            mm.IsBodyHtml      = true;
            mm.Body            = corpoHtml;
            mm.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;

            if (_modoTeste)
            {
                // Em ModoTeste TODOS os emails vão somente para EmailTeste — nunca para destinatários reais
                mm.To.Add(_emailTeste);
                mm.Subject = "[TESTE] " + assunto;
            }
            else
            {
                foreach (var dest in destinatarios)
                    mm.To.Add(dest);

                if (cc is not null)
                    foreach (var c in cc)
                        mm.CC.Add(c);

                if (replyTo is not null)
                    foreach (var r in replyTo)
                        mm.ReplyToList.Add(new MailAddress(r));
            }

            if (anexosCaminhos is not null)
                foreach (var caminho in anexosCaminhos)
                    if (!string.IsNullOrWhiteSpace(caminho) && File.Exists(caminho))
                        mm.Attachments.Add(new Attachment(caminho));

            client.Send(mm);
            return null; // sucesso
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ── Layout HTML padrão CRIAP ─────────────────────────────────────────────
    public string ConstruirLayoutHtml(string titulo, string conteudo, bool rodapeInterno = false, string? versao = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html>");
        sb.AppendLine("<head><meta charset='utf-8'>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body  { font-family: Arial, sans-serif; font-size: 13px; color: #333; margin: 0; padding: 0; }");
        sb.AppendLine("  .header { background: #ed7520; padding: 14px 24px; }");
        sb.AppendLine("  .header h2 { color: #fff; margin: 0; font-size: 15px; font-weight: bold; }");
        sb.AppendLine("  .content { padding: 20px 24px; }");
        sb.AppendLine("  table { border-collapse: collapse; width: 100%; }");
        sb.AppendLine("  table th { background: #ed7520; color: #fff; padding: 7px 12px; text-align: left; border: 1px solid #d4641a; }");
        sb.AppendLine("  table td { padding: 6px 12px; border: 1px solid #eee; }");
        sb.AppendLine("  .footer { font-size: 11px; color: #999; padding: 10px 24px 16px; border-top: 2px solid #ed7520; margin-top: 20px; }");
        sb.AppendLine("</style></head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class='content'>");
        sb.AppendLine(conteudo);
        sb.AppendLine("  </div>");
        if (rodapeInterno)
        {
            sb.AppendLine($"  <div class='footer'>Instituto CRIAP &mdash; envio autom&aacute;tico");
            if (!string.IsNullOrWhiteSpace(versao))
                sb.AppendLine($"<br><small>{versao}</small>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ── SMTP Client ──────────────────────────────────────────────────────────
    private SmtpClient CriarSmtpClient() => new()
    {
        Host           = _cfg.SmtpHost,
        Port           = _cfg.SmtpPort,
        Timeout        = 15_000,
        DeliveryMethod = SmtpDeliveryMethod.Network,
        Credentials    = new NetworkCredential(_cfg.Remetente, _cfg.Password)
    };
}
