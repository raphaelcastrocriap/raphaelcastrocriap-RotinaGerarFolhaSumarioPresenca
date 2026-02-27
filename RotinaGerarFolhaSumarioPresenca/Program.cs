using Microsoft.Extensions.Configuration;
using RotinaGerarFolhaSumarioPresenca.Models;
using RotinaGerarFolhaSumarioPresenca.Rotinas.GerarFolhaSumarioPresenca;

// ═══════════════════════════════════════════════════════════════════════════════
//  Ponto de entrada
//
//  O appsettings.json contém apenas infraestrutura (SMTP, BDs, URLs de API).
//  Para testar ou alterar comportamento, edite as constantes no topo da Rotina.
// ═══════════════════════════════════════════════════════════════════════════════

Console.OutputEncoding = System.Text.Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var cfg = configuration.Get<AppSettings>()
    ?? throw new InvalidOperationException("Falha ao carregar appsettings.json");

Console.WriteLine($"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] Iniciando {nameof(GerarFolhaPresencaRotina)}...");
Console.WriteLine();

try
{
    var rotina = new GerarFolhaPresencaRotina(cfg);
    await rotina.ExecutarAsync();
    Console.WriteLine($"\n[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] Rotina concluída.");
}
catch (Exception ex)
{
    // Proteção final — a rotina em si nunca deveria lançar aqui, mas garantimos.
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FATAL] Erro não tratado: {ex}");
    Console.ResetColor();
    Environment.Exit(1);
}
