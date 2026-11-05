using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Processamento;
using HubPedidos.Infrastructure.Consultas;
using HubPedidos.Infrastructure.Persistencia;
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
