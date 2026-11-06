using HubPedidos.Api.Cache;
using HubPedidos.Api.Contratos.V2;
using HubPedidos.Api.Seguranca;
using HubPedidos.Api.Versionamento;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Domain.Pedidos;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api.Endpoints;

/// <summary>
/// Consulta de pedido em duas versões que coexistem (Roteiro 6). As duas usam a mesma
/// porta de consulta; só a representação muda.
/// </summary>
public static class ConsultaDePedidoVersionadaEndpoints
{
    public static IEndpointRouteBuilder MapConsultaDePedidoVersionada(this IEndpointRouteBuilder rotas)
    {
        // Versão ausente: rota anterior ao versionamento, tratada como v1 (padrão).
        rotas.MapGet("/api/pedidos/{id}", (PedidoId id, IConsultasDePedidos consultas, HttpResponse resposta, CancellationToken ct) =>
                ObterV1Async(id, consultas, resposta, versaoImplicita: true, ct))
            .WithName("ObterPedido").WithGroupName("v1");

        rotas.MapGet("/api/v1/pedidos/{id}", (PedidoId id, IConsultasDePedidos consultas, HttpResponse resposta, CancellationToken ct) =>
                ObterV1Async(id, consultas, resposta, versaoImplicita: false, ct))
            .WithName("ObterPedidoV1").WithGroupName("v1");

        // Roteiro 7: a v2 (usada pela tela de acompanhamento) exige autenticação e tem ETag.
        rotas.MapGet("/api/v2/pedidos/{id}", ObterV2Async)
            .WithName("ObterPedidoV2").WithGroupName("v2")
            .RequireAuthorization()
            .Produces<PedidoResponseV2>()
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status404NotFound);

        // Extensão do Roteiro 7: atualização otimista com If-Match (412 se a representação mudou).
        rotas.MapPost("/api/v2/pedidos/{id}/cancelamento", CancelarV2Async)
            .WithName("CancelarPedidoV2").WithGroupName("v2")
            .RequireAuthorization();

        // Qualquer outra versão: política explícita. Segmentos literais (v1, v2) têm
        // precedência sobre este padrão no roteamento.
        rotas.MapGet("/api/v{versao:int}/pedidos/{id}", VersaoNaoSuportada)
            .ExcludeFromDescription();

        return rotas;
    }

    public static async Task<Results<Ok<PedidoResponse>, NotFound>> ObterV1Async(
        PedidoId id, IConsultasDePedidos consultas, HttpResponse resposta, bool versaoImplicita, CancellationToken cancellationToken)
    {
        PoliticaDeVersao.RegistrarUso(1, versaoImplicita);
        PoliticaDeVersao.AplicarCabecalhosDaV1(resposta, id.ToString());
        return await consultas.ObterDetalheAsync(id, cancellationToken) is { } pedido
            ? TypedResults.Ok(pedido)
            : TypedResults.NotFound();
    }

    /// <summary>
    /// v2 com validação condicional. Ordem: autorização PRIMEIRO (o usuário pode ver este
    /// pedido?), depois a representação, a ETag e a comparação com If-None-Match. Assim um
    /// 304 nunca confirma a existência de um pedido de outro cliente.
    /// </summary>
    public static async Task<IResult> ObterV2Async(
        PedidoId id, AutorizacaoDePedidos autorizacao, HttpContext http, CancellationToken cancellationToken)
    {
        PoliticaDeVersao.RegistrarUso(2, versaoImplicita: false);
        http.Response.Headers[PoliticaDeVersao.CabecalhoDeVersoesSuportadas] = PoliticaDeVersao.VersoesSuportadasComoTexto;

        // Pedido inexistente e pedido de outro cliente recebem a mesma resposta (404).
        if (await autorizacao.ObterSePermitidoAsync(http.User, id, cancellationToken) is not { } pedido)
        {
            return TypedResults.NotFound();
        }

        var (corpo, etag) = ValidacaoCondicional.Serializar(AdaptadorDePedidoV2.Converter(pedido));
        ValidacaoCondicional.AplicarCabecalhos(http.Response, etag);
        return ValidacaoCondicional.IfNoneMatchCorresponde(http.Request, etag)
            ? TypedResults.StatusCode(StatusCodes.Status304NotModified) // sem corpo; ETag e Cache-Control preservados
            : TypedResults.Bytes(corpo, "application/json; charset=utf-8");
    }

    /// <summary>
    /// Cancelamento com pré-condição: se o cliente enviar If-Match e a representação atual
    /// tiver outra ETag (alguém mudou o pedido desde a última leitura), responde 412 sem
    /// alterar nada. Sem If-Match, o cancelamento segue a regra normal.
    /// </summary>
    /// <remarks>
    /// Limitação: a comparação e o cancelamento são dois passos; uma mudança entre eles
    /// ainda é barrada pela regra do domínio (só "Recebido" pode ser cancelado), mas não
    /// pela ETag. Uma garantia atômica exigiria um token de concorrência na tabela.
    /// </remarks>
    public static async Task<IResult> CancelarV2Async(
        PedidoId id, AutorizacaoDePedidos autorizacao, ServicoDePedidos servico, HttpContext http, CancellationToken cancellationToken)
    {
        if (await autorizacao.ObterSePermitidoAsync(http.User, id, cancellationToken) is not { } pedido)
        {
            return TypedResults.NotFound();
        }

        var (_, etagAtual) = ValidacaoCondicional.Serializar(AdaptadorDePedidoV2.Converter(pedido));
        if (ValidacaoCondicional.IfMatchCorresponde(http.Request, etagAtual) is false)
        {
            ValidacaoCondicional.AplicarCabecalhos(http.Response, etagAtual);
            return TypedResults.Problem(
                "O pedido mudou desde a sua última leitura. Consulte novamente antes de cancelar.",
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: "Pré-condição falhou");
        }

        return await servico.CancelarAsync(id, cancellationToken) switch
        {
            ResultadoDoCancelamento.Cancelado => TypedResults.NoContent(),
            ResultadoDoCancelamento.NaoEncontrado => TypedResults.NotFound(),
            _ => TypedResults.Problem("A situação atual do pedido não permite cancelamento.", statusCode: StatusCodes.Status409Conflict),
        };
    }

    public static ProblemHttpResult VersaoNaoSuportada(int versao, HttpResponse resposta)
    {
        resposta.Headers[PoliticaDeVersao.CabecalhoDeVersoesSuportadas] = PoliticaDeVersao.VersoesSuportadasComoTexto;
        return TypedResults.Problem(
            $"A versão {versao} da API não é suportada. Versões suportadas: {PoliticaDeVersao.VersoesSuportadasComoTexto}.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Versão não suportada");
    }
}
