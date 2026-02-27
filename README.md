# Rotina — Gerar Folha Sumário de Presença (F029)

> **Este projeto é o modelo oficial de rotinas automatizadas do Instituto CRIAP.**
> Para criar uma nova rotina, basta clonar este repositório e seguir o guia na secção [Criar uma nova rotina](#criar-uma-nova-rotina).

---

## Índice

1. [Visão geral](#visão-geral)
2. [Estrutura do projeto](#estrutura-do-projeto)
3. [O que esta rotina faz](#o-que-esta-rotina-faz)
4. [Como executar](#como-executar)
5. [Modo Teste vs Produção](#modo-teste-vs-produção)
6. [Configuração — appsettings.json](#configuração--appsettingsjson)
7. [Bases de dados disponíveis](#bases-de-dados-disponíveis)
8. [Regras que nunca devem ser quebradas](#regras-que-nunca-devem-ser-quebradas)
9. [Criar uma nova rotina](#criar-uma-nova-rotina)

---

## Visão geral

Rotina console .NET 9 que executa diariamente e:

1. Consulta no **HumaTrain (HT)** as sessões presenciais do dia.
2. Para cada sessão, chama a **API DTP** para gerar a Folha Sumário de Presença (F029) preenchida.
3. Envia o **PDF por email ao formador** responsável.
4. Grava o **log de cada ação** realizada na base `secretariavirtual`.
5. Envia um **email de relatório final** (com resultados + log de execução) para Informática e Pedagógico.

Erros nunca param a rotina — são registados e incluídos no relatório.

---

## Estrutura do projeto

```
RotinaGerarFolhaSumarioPresenca/
│
├── appsettings.json                          ← Todas as configurações (email, BD, API)
├── Program.cs                                ← Ponto de entrada — carrega config e arranca a rotina
│
├── Models/
│   ├── AppSettings.cs                        ← POCOs de configuração + DbKey (constantes de BD)
│   ├── SessaoFormador.cs                     ← Modelo do resultado SQL
│   ├── ApiModels.cs                          ← Request/Response da API F029
│   └── RelatorioItem.cs                      ← Uma linha do relatório final
│
├── Infrastructure/
│   ├── Database/
│   │   ├── SqlServerHelper.cs                ← Wrapper SQL Server  (HT, SV, DTP, Portais)
│   │   └── MySqlHelper.cs                    ← Wrapper MySQL       (Moodle, CRM)
│   ├── Email/
│   │   └── EmailService.cs                   ← Envio SMTP + template HTML CRIAP
│   └── Logging/
│       └── AppLogger.cs                      ← Log console (colorido) + buffer HTML p/ relatório
│
├── Services/
│   └── LogDbService.cs                       ← Grava ações realizadas em sv_logs (SecretariaVirtual)
│
└── Rotinas/
    └── GerarFolhaSumarioPresenca/
        └── RotinaGerarFolhaSumarioPresenca.cs ← Lógica completa desta rotina
```

---

## O que esta rotina faz

```
Início
  │
  ├─ Resolve a data alvo (hoje, ou DataFiltroOverride definido na rotina)
  │
  ├─ SQL → HumaTrain
  │    Sessões presenciais (Comp_elr = 'P') da data alvo
  │    Excluindo formadores internos definidos em FormadoresExcluidos (constante na rotina)
  │
  ├─ [Para cada grupo de sessões da mesma RefAccao]
  │    │
  │    ├─ POST /api/v2/acoes-dtp/gerar-f029-preenchido
  │    │    Body: { "refAcao": "...", "rowIdsSessoes": [...] }
  │    │
  │    ├─ [Para cada sessão gerada com sucesso]
  │    │    ├─ Envia email ao formador (PDF em anexo — caminho UNC de rede)
  │    │    │    CC: tecnicopedagogico@criap.com, informatica@criap.com
  │    │    └─ Grava ação em sv_logs (SecretariaVirtual)
  │    │
  │    └─ [Erros de API ou geração] → registados no relatório, rotina continua
  │
  └─ Envia email de relatório final
       Para: informatica@criap.com, tecnicopedagogico@criap.com
       Conteúdo: tabela de resultados + log completo de execução
```

---

## Como executar

### Pré-requisitos
- .NET 9 SDK
- Acesso à rede interna (192.168.1.248, 192.168.1.213)

### Desenvolvimento / Teste

Edite as constantes no topo do ficheiro da rotina antes de executar:

```csharp
// Em: Rotinas/GerarFolhaSumarioPresenca/RotinaGerarFolhaSumarioPresenca.cs
private const bool   ModoTeste          = true;           // ← ligar modo teste
private const string DataFiltroOverride = "2026-01-10";   // ← forçar uma data
private const string EmailTeste         = "raphaelcastro@criap.com";
```

Depois executar normalmente:

```powershell
dotnet run
```

> ⚠️ **Nunca fazer deploy com `ModoTeste = true`.**

### Produção (publicar)

```powershell
dotnet publish -c Release -o ./publish
```

O executável gerado em `./publish` pode ser agendado no **Agendador de Tarefas do Windows** sem argumentos — usará as configurações do `appsettings.json` que acompanha o executável.

---

## Modo Teste vs Produção

Todo o controlo de teste está nas **constantes no topo do ficheiro da rotina** — o `appsettings.json` é apenas infraestrutura e nunca precisa de ser alterado para testes.

| Constante | Produção | Teste |
|---|---|---|
| `ModoTeste` | `false` | `true` |
| `DataFiltroOverride` | `""` | `"2026-01-10"` |

```csharp
// Rotinas/GerarFolhaSumarioPresenca/RotinaGerarFolhaSumarioPresenca.cs
private const bool   ModoTeste          = false;          // ← mudar para true em teste
private const string DataFiltroOverride = "";             // ← ou "2026-01-10"
private const string EmailInformatica   = "informatica@criap.com";
private const string EmailPedagogico    = "tecnicopedagogico@criap.com";
private const string EmailTeste         = "raphaelcastro@criap.com";
```

> ⚠️ **Nunca fazer deploy com `ModoTeste = true`.**

### O que muda no Modo Teste

| Comportamento | Produção | Teste |
|---|---|---|
| Destinatário dos emails | Formador real + CC Pedagógico | Só `raphaelcastro@criap.com` |
| Assunto do email | Normal | Prefixado com `[TESTE]` |
| URL da API | `http://192.168.1.213:8080` | `http://localhost:5141` |
| Base de dados | HT produção (192.168.1.248) | *(mesma — usar `HT_Test` se necessário)* |
| Log na BD | Grava em `sv_logs` normalmente | Grava em `sv_logs` normalmente |

> Para usar a BD de teste, basta trocar `DbKey.HT` por `DbKey.HT_Test` no construtor da rotina durante os testes.

---

## Configuração — appsettings.json

O `appsettings.json` contém **apenas infraestrutura** (credenciais SMTP, connection strings, URL da API). Nunca é necessário alterá-lo para testes — as variáveis de teste estão na rotina.

```jsonc
{
  "Email": {
    "Remetente": "noreply@criap.com",
    "NomeRemetente": "Instituto CRIAP",
    "Password": "...",
    "SmtpHost": "mail.criap.com",
    "SmtpPort": 25
  },

  "ConnectionStrings": { /* ver secção abaixo */ },

  "Api": {
    "BaseUrl":       "http://192.168.1.213:8080",  // produção
    "BaseUrlTeste":  "http://localhost:5141",       // usado automaticamente quando ModoTeste=true
    "TimeoutSeconds": 60
  }
}
```

> Os destinatários de email (`EmailInformatica`, `EmailPedagogico`, `EmailTeste`) e os flags de controlo (`ModoTeste`, `DataFiltroOverride`) são constantes privadas em cada ficheiro de rotina, não campos do `appsettings.json`.

---

## Bases de dados disponíveis

Use as constantes de `DbKey` para referenciar as bases sem digitar strings:

| Constante | Base | Tipo | Servidor |
|---|---|---|---|
| `DbKey.HT` | HumaTrain (produção) | SQL Server | 192.168.1.248 |
| `DbKey.SV` | SecretariaVirtual (produção) | SQL Server | 192.168.1.248 |
| `DbKey.DtpDigital` | AnaliseDTP | SQL Server | 192.168.1.248 |
| `DbKey.PortalAluno` | Portal do Aluno | SQL Server | portaldoalunocriap.com |
| `DbKey.PortalDoProfessor` | Portal do Professor | SQL Server | portaldoalunocriap.com |
| `DbKey.Moodle` | Moodle (atual) | **MySQL** | 94.46.28.131 |
| `DbKey.MoodleNew` | Moodle (novo) | **MySQL** | 94.46.171.68 |
| `DbKey.CRM` | SuiteCRM | **MySQL** | 94.46.28.119 |
| `DbKey.HT_Test` | HumaTrain (teste) | SQL Server | 192.168.1.214 |
| `DbKey.SV_Test` | SecretariaVirtual (teste) | SQL Server | 192.168.1.214 |

**SQL Server** → usar `SqlServerHelper`
**MySQL** → usar `MySqlHelper`

```csharp
// Exemplos de uso dentro de uma rotina
var dbHT     = new SqlServerHelper(cfg.GetConnectionString(DbKey.HT));
var dbMoodle = new MySqlHelper(cfg.GetConnectionString(DbKey.Moodle));
```

---

## Regras que nunca devem ser quebradas

Estas regras aplicam-se a **todas** as rotinas da empresa:

1. **A rotina nunca termina por exceção não tratada.**
   Todo o código de negócio deve estar dentro de `try/catch`. Erros são logados e a execução continua para o próximo item.

2. **Erros sempre chegam ao email de Informática.**
   Use `_logger.Erro(...)` — o callback de erro envia automaticamente um email para `informatica@criap.com`.

3. **O log de BD regista apenas ações realizadas com sucesso.**
   Erros vão para o email de relatório e para o log de consola, não para `sv_logs`.

4. **O relatório final é sempre enviado**, mesmo que não haja nada processado ou que tudo tenha falhado.

5. **Em ModoTeste, nenhum email real é enviado.** O `EmailService` redireciona tudo automaticamente para `EmailTeste`.

6. **Configuração específica de rotina fica na rotina**, não no `appsettings.json`. Exemplo: `FormadoresExcluidos` é uma constante privada da rotina que a usa, não um campo de configuração global.

---

## Criar uma nova rotina

### Passo 1 — Clonar o repositório

```powershell
git clone <url-do-repo> NomeDaNovaRotina
cd NomeDaNovaRotina
```

Renomear a solução e o projeto conforme necessário.

### Passo 2 — Criar o ficheiro da rotina

Copiar `Rotinas/GerarFolhaSumarioPresenca/RotinaGerarFolhaSumarioPresenca.cs` para uma nova pasta e renomear:

```
Rotinas/
  MinhaNovaRotina/
    MinhaNovaRotina.cs
```

### Passo 3 — Adaptar a classe

No topo da nova classe, alterar:

```csharp
// ── Identificação
private const string NOME_ROTINA  = "MinhaNovaRotina";
private const string VERSAO       = "1.0.0";
private const string API_ENDPOINT = "/api/v2/...";   // se usar API

// ── CONSTANTES DE DESENVOLVIMENTO — as únicas que tocas para testar
private const bool   ModoTeste          = false;
private const string DataFiltroOverride = "";
private const string EmailInformatica   = "informatica@criap.com";
private const string EmailPedagogico    = "tecnicopedagogico@criap.com";
private const string EmailTeste         = "raphaelcastro@criap.com";
```

### Passo 4 — Adaptar o Program.cs

```csharp
var rotina = new MinhaNovaRotina(cfg);
await rotina.ExecutarAsync();
```

### Passo 5 — Testar

Edite as constantes no topo da classe da rotina:

```csharp
private const bool   ModoTeste          = true;
private const string DataFiltroOverride = "2026-01-10";
```

Depois:

```powershell
dotnet run
```

### Passo 6 — Publicar

```powershell
dotnet publish -c Release -o ./publish
```

Copiar a pasta `publish/` para o servidor e agendar no **Agendador de Tarefas do Windows**:
- Ação: `RotinaGerarFolhaSumarioPresenca.exe`
- Iniciar em: pasta onde está o executável (para o `appsettings.json` ser encontrado)
- Agendar conforme necessário (diário, horário, etc.)

---

*Instituto CRIAP — Departamento de Informática*
