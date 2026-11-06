using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Processamento;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Infrastructure.Consultas;
using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Infrastructure.Processamento;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// O catálogo é construído uma única vez (singleton). A reflexão acontece aqui,
// na inicialização, e não a cada requisição.
builder.Services.AddSingleton(_ => CatalogoDeProcessadores.DescobrirPadrao());
builder.Services.AddSingleton(provedor =>
    new ServicoDeRecepcaoDePedidos(provedor.GetRequiredService<CatalogoDeProcessadores>()));

// Roteiro 2: importação de lotes. Limites lidos da seção "Importacao" (com padrões
// seguros no código). O importador não guarda estado entre chamadas: singleton.
var opcoesDeImportacao = builder.Configuration.GetSection("Importacao").Get<OpcoesDeImportacao>() ?? OpcoesDeImportacao.Padrao;
opcoesDeImportacao.Validar();
builder.Services.AddSingleton(opcoesDeImportacao);
builder.Services.AddSingleton(provedor => new ImportadorDeLotesPorFluxo(provedor.GetRequiredService<OpcoesDeImportacao>()));
builder.Services.AddSingleton<ProcessadorDeLotes>();

// Roteiro 3: persistência com EF Core + SQLite. A connection string NÃO está no
// appsettings: vem de User Secrets (desenvolvimento) ou da variável de ambiente
// ConnectionStrings__HubPedidos. Sem ela, a aplicação não sobe.
var connectionString = builder.Configuration.GetConnectionString("HubPedidos")
    ?? throw new InvalidOperationException(
        "Connection string 'HubPedidos' não configurada. Execute scripts/configurar-desenvolvimento.ps1 " +
        "ou defina a variável de ambiente ConnectionStrings__HubPedidos.");

// DbContext é scoped: um por requisição, nunca compartilhado entre threads.
// Sem EnableSensitiveDataLogging: o log de comandos mostra o SQL com parâmetros, sem valores.
builder.Services.AddDbContext<ContextoDePedidos>(opcoes => opcoes.UseSqlite(connectionString));
builder.Services.AddScoped<RepositorioDePedidos>();
builder.Services.AddScoped<ConsultasDePedidos>();

// Roteiro 4: fila limitada, consumidores e limite de acesso ao banco.
// Singletons: estado compartilhado por todas as requisições e consumidores.
// O processamento de cada item é scoped (um escopo por item, com seu DbContext).
var opcoesDaFila = builder.Configuration.GetSection("Fila").Get<OpcoesDaFila>() ?? new OpcoesDaFila();
opcoesDaFila.Validar();
builder.Services.AddSingleton(opcoesDaFila);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MetricasDoProcessamento>();
builder.Services.AddSingleton<FilaDeProcessamento>();
builder.Services.AddSingleton(_ => new LimitadorDeAcessoAoBanco(opcoesDaFila.LimiteDeAcessoAoBanco));
builder.Services.AddScoped<IProcessamentoDePedido, ProcessamentoDePedidoEfCore>();
builder.Services.AddHostedService<ProcessadorDePedidosWorker>();
// Prazo para drenar a fila no encerramento; depois dele o cancelamento é forçado.
builder.Services.Configure<HostOptions>(opcoes =>
    opcoes.ShutdownTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Fila:PrazoDeEncerramentoEmSegundos", 15)));

var app = builder.Build();

// Resolve o catálogo antes de aceitar requisições: uma configuração inválida
// (duplicidade, metadado inconsistente, categoria sem processador) derruba a
// inicialização com mensagem legível, em vez de falhar na primeira chamada.
var catalogo = app.Services.GetRequiredService<CatalogoDeProcessadores>();
app.Logger.LogInformation(
    "Catálogo de processadores pronto: {Quantidade} registros, {Ativos} ativos.",
    catalogo.Entradas.Count,
    catalogo.Ativos.Count);

await PrepararBancoAsync(app, args);
if (args.Contains("--somente-preparar-banco"))
{
    return;
}

// Roteiro 5: profundidade da fila como métrica observável (dotnet-counters).
var filaDeProcessamento = app.Services.GetRequiredService<FilaDeProcessamento>();
HubPedidos.Application.Diagnostico.TelemetriaDoHub.PublicarProfundidadeDaFila(() => filaDeProcessamento.Profundidade);

// Erros de binding (ex.: PedidoId inválido na rota) chegam como BadHttpRequestException
// com status 400; sem o seletor, o manipulador global os transformaria em 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = excecao => excecao is BadHttpRequestException requisicaoInvalida
        ? requisicaoInvalida.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapPedidosEndpoints();
app.MapProcessadoresEndpoints();
app.MapProcessamentoEndpoints();

await app.RunAsync();

// Aplica migrations quando configurado (Development) e, com "--semear N", gera massa de dados.
static async Task PrepararBancoAsync(WebApplication app, string[] args)
{
    await using var escopo = app.Services.CreateAsyncScope();
    var contexto = escopo.ServiceProvider.GetRequiredService<ContextoDePedidos>();

    if (app.Configuration.GetValue<bool>("Banco:AplicarMigracoesNaInicializacao"))
    {
        await contexto.Database.MigrateAsync();
        app.Logger.LogInformation("Migrations aplicadas.");
    }

    // Roteiro 5: modo de journal do SQLite explícito ("DELETE" é o padrão do SQLite;
    // "WAL" foi a intervenção avaliada). O modo WAL fica gravado no arquivo do banco.
    if (app.Configuration["Banco:ModoJournal"] is { Length: > 0 } modo)
    {
        if (modo is not ("DELETE" or "WAL"))
        {
            throw new InvalidOperationException("Banco:ModoJournal deve ser DELETE ou WAL.");
        }

        // A conexão pertence ao contexto: abre e fecha por ele, sem descartá-la.
        await contexto.Database.OpenConnectionAsync();
        await using (var comando = contexto.Database.GetDbConnection().CreateCommand())
        {
            comando.CommandText = $"PRAGMA journal_mode={modo};";
            app.Logger.LogInformation("SQLite journal_mode = {Modo}.", await comando.ExecuteScalarAsync());
        }

        await contexto.Database.CloseConnectionAsync();
    }

    var indice = Array.IndexOf(args, "--semear");
    if (indice >= 0 && indice + 1 < args.Length && int.TryParse(args[indice + 1], out var quantidade))
    {
        var total = await SemeadorDePedidos.SemearAsync(
            contexto, app.Services.GetRequiredService<CatalogoDeProcessadores>(), quantidade, DateTime.UtcNow);
        app.Logger.LogInformation("Massa de dados gerada: {Total} pedidos no banco.", total);
    }
}

/// <summary>Declaração parcial que expõe o ponto de entrada para testes futuros.</summary>
public partial class Program;
