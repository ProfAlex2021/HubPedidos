using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api.Endpoints;

/// <summary>Roteiro 4: produtores da fila e métricas do processamento.</summary>
public static class ProcessamentoEndpoints
{
    /// <summary>Sugestão ao cliente de quando tentar de novo após uma rejeição por fila cheia.</summary>
    public const int SegundosParaNovaTentativa = 1;

    public static IEndpointRouteBuilder MapProcessamentoEndpoints(this IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/api/pedidos/{id}/processamento", SolicitarProcessamentoAsync).WithName("SolicitarProcessamento").WithGroupName("v1");
        rotas.MapPost("/api/processamento/pendentes", EnfileirarPendentesAsync).WithName("EnfileirarPendentes").WithGroupName("v1");
        rotas.MapGet("/api/processamento/metricas", ObterMetricas).WithName("MetricasDoProcessamento").WithGroupName("v1");
        return rotas;
    }

    /// <summary>
    /// Produtor interativo: admissão IMEDIATA com TryWrite. Fila cheia vira 429 com
    /// Retry-After, sem prender a requisição nem descartar o pedido em silêncio. O item
    /// leva só o identificador: nenhum DbContext ou entidade vai para a fila.
    /// </summary>
    public static async Task<Results<Accepted, NotFound, ProblemHttpResult>> SolicitarProcessamentoAsync(
        PedidoId id,
        FilaDeProcessamento fila,
        IRepositorioDePedidos repositorio,
        HttpResponse resposta,
        CancellationToken cancellationToken)
    {
        if (!await repositorio.ExisteAsync(id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        switch (fila.TentarEnfileirar(id))
        {
            case ResultadoDaAdmissao.Aceito:
                return TypedResults.Accepted($"/api/pedidos/{id}");

            case ResultadoDaAdmissao.Rejeitado:
                resposta.Headers.RetryAfter = SegundosParaNovaTentativa.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return TypedResults.Problem(
                    $"A fila de processamento está cheia ({fila.Opcoes.Capacidade} itens). Tente novamente em instantes.",
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Fila cheia");

            default:
                return TypedResults.Problem(
                    "A aplicação está encerrando e não aceita novos itens.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Encerrando");
        }
    }

    /// <summary>
    /// Produtor interno em lote: enfileira pedidos ainda "Recebido" com WriteAsync, que
    /// AGUARDA espaço quando a fila está cheia (backpressure até o cliente HTTP). Se a
    /// requisição for cancelada, a espera é interrompida e os itens restantes não entram.
    /// </summary>
    public static async Task<Results<Ok<int>, ProblemHttpResult>> EnfileirarPendentesAsync(
        FilaDeProcessamento fila,
        IRepositorioDePedidos repositorio,
        CancellationToken cancellationToken,
        int limite = 500)
    {
        if (limite is < 1 or > 10_000)
        {
            return TypedResults.Problem("O limite deve estar entre 1 e 10.000.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Os identificadores são lidos antes: nenhum DbContext fica aberto enquanto espera a fila.
        var pendentes = await repositorio.ListarPendentesAsync(limite, cancellationToken);

        var enfileirados = 0;
        try
        {
            foreach (var id in pendentes)
            {
                await fila.EnfileirarAsync(id, cancellationToken);
                enfileirados++;
            }
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            return TypedResults.Problem($"Encerrando: {enfileirados} itens foram enfileirados antes.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return TypedResults.Ok(enfileirados);
    }

    public static Ok<InstantaneoDasMetricas> ObterMetricas(
        FilaDeProcessamento fila, MetricasDoProcessamento metricas, LimitadorDeAcessoAoBanco limitador) =>
        TypedResults.Ok(metricas.Instantaneo(fila.Profundidade, limitador.Pico));
}
