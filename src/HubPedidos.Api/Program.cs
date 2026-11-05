using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Processamento;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// O catálogo é construído uma única vez (singleton). A reflexão acontece aqui,
// na inicialização, e não a cada requisição.
builder.Services.AddSingleton(_ => CatalogoDeProcessadores.DescobrirPadrao());
builder.Services.AddSingleton(provedor =>
    new ServicoDeRecepcaoDePedidos(provedor.GetRequiredService<CatalogoDeProcessadores>()));

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
