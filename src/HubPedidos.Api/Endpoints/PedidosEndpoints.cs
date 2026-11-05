using HubPedidos.Application.Importacao;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Contratos;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api.Endpoints;

/// <summary>Endpoints de pedidos (Minimal APIs).</summary>
public static class PedidosEndpoints
{
    public static IEndpointRouteBuilder MapPedidosEndpoints(this IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/api/pedidos", ReceberPedido)
            .WithName("ReceberPedido");

        // Roteiro 2: o corpo é lido como stream (sem binding para string) e a resposta
        // é escrita progressivamente em NDJSON.
        rotas.MapPost("/api/pedidos/lotes", ImportarLoteAsync)
            .WithName("ImportarLote")
            .Accepts<string>("text/plain")
            .Produces(StatusCodes.Status200OK, contentType: "application/x-ndjson");

        return rotas;
    }

    /// <summary>
    /// Processa um lote em formato delimitado sem carregar o corpo inteiro na memória:
    /// cada linha é interpretada, processada e escrita assim que chega.
    /// </summary>
    public static async Task ImportarLoteAsync(HttpContext contexto, ProcessadorDeLotes processador, CancellationToken cancellationToken)
    {
        contexto.Response.StatusCode = StatusCodes.Status200OK;
        contexto.Response.ContentType = "application/x-ndjson; charset=utf-8";
        await processador.ProcessarAsync(contexto.Request.Body, contexto.Response.Body, cancellationToken);
    }

    /// <summary>
    /// Recebe o DTO, delega ao caso de uso e traduz o resultado para HTTP.
    /// O tipo de retorno <c>Results&lt;...&gt;</c> documenta todas as respostas possíveis
    /// e nenhum tipo do domínio aparece na resposta.
    /// </summary>
    public static Results<Ok<PedidoResponse>, ValidationProblem, ProblemHttpResult> ReceberPedido(
        CriarPedidoRequest? requisicao,
        ServicoDeRecepcaoDePedidos servico)
    {
        // Pattern matching por tipo sobre a hierarquia fechada de resultados.
        return servico.Receber(requisicao) switch
        {
            ResultadoDaRecepcao.Aceito aceito => TypedResults.Ok(aceito.Pedido),
            ResultadoDaRecepcao.DadosInvalidos invalidos => TypedResults.ValidationProblem(
                invalidos.Erros
                    .GroupBy(erro => erro.Campo)
                    .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(erro => erro.Mensagem).ToArray())),
            ResultadoDaRecepcao.Rejeitado rejeitado => TypedResults.Problem(
                rejeitado.Motivo,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Pedido não classificável"),
            _ => throw new InvalidOperationException("Resultado de recepção desconhecido."),
        };
    }
}
