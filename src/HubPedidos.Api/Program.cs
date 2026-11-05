using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Processamento;

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

var app = builder.Build();

// Resolve o catálogo antes de aceitar requisições: uma configuração inválida
// (duplicidade, metadado inconsistente, categoria sem processador) derruba a
// inicialização com mensagem legível, em vez de falhar na primeira chamada.
var catalogo = app.Services.GetRequiredService<CatalogoDeProcessadores>();
app.Logger.LogInformation(
    "Catálogo de processadores pronto: {Quantidade} registros, {Ativos} ativos.",
    catalogo.Entradas.Count,
    catalogo.Ativos.Count);

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapPedidosEndpoints();
app.MapProcessadoresEndpoints();

app.Run();

/// <summary>Declaração parcial que expõe o ponto de entrada para testes futuros.</summary>
public partial class Program;
