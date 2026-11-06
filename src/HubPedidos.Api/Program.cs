using HubPedidos.Api.Composicao;
using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Diagnostico;
using HubPedidos.Application.Processamento;
using HubPedidos.Application.Processamento.Fila;

var builder = WebApplication.CreateBuilder(args);

// Roteiro 6: todos os registros ficam no composition root.
ComposicaoDaAplicacao.ConfigurarServicos(builder.Services, builder.Configuration);

// Valida o grafo de dependências na construção (inclusive em Production): um serviço
// que não pode ser criado ou um scoped capturado por singleton derruba a inicialização.
builder.Host.UseDefaultServiceProvider(opcoes =>
{
    opcoes.ValidateOnBuild = true;
    opcoes.ValidateScopes = true;
});

var app = builder.Build();

// Resolve o catálogo antes de aceitar requisições: uma configuração inválida
// (duplicidade, metadado inconsistente, categoria sem processador) derruba a
// inicialização com mensagem legível, em vez de falhar na primeira chamada.
var catalogo = app.Services.GetRequiredService<CatalogoDeProcessadores>();
app.Logger.LogInformation(
    "Catálogo de processadores pronto: {Quantidade} registros, {Ativos} ativos.",
    catalogo.Entradas.Count,
    catalogo.Ativos.Count);

await PreparacaoDoBanco.ExecutarAsync(app, args);
if (args.Contains("--somente-preparar-banco"))
{
    return;
}

// Roteiro 5: profundidade da fila como métrica observável (dotnet-counters).
var fila = app.Services.GetRequiredService<FilaDeProcessamento>();
TelemetriaDoHub.PublicarProfundidadeDaFila(() => fila.Profundidade);

// Erros de binding (ex.: PedidoId inválido na rota) chegam como BadHttpRequestException
// com status 400; sem o seletor, o manipulador global os transformaria em 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = excecao => excecao is BadHttpRequestException requisicaoInvalida
        ? requisicaoInvalida.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapOpenApi(); // /openapi/v1.json e /openapi/v2.json
app.MapPedidosEndpoints();
app.MapConsultaDePedidoVersionada();
app.MapProcessadoresEndpoints();
app.MapProcessamentoEndpoints();

await app.RunAsync();

/// <summary>Declaração parcial que expõe o ponto de entrada para o WebApplicationFactory.</summary>
public partial class Program;
