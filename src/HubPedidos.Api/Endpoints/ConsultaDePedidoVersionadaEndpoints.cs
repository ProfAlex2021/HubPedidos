using HubPedidos.Api.Contratos.V2;
using HubPedidos.Api.Versionamento;
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

        rotas.MapGet("/api/v2/pedidos/{id}", ObterV2Async)
            .WithName("ObterPedidoV2").WithGroupName("v2");

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

    public static async Task<Results<Ok<PedidoResponseV2>, NotFound>> ObterV2Async(
        PedidoId id, IConsultasDePedidos consultas, HttpResponse resposta, CancellationToken cancellationToken)
    {
        PoliticaDeVersao.RegistrarUso(2, versaoImplicita: false);
        resposta.Headers[PoliticaDeVersao.CabecalhoDeVersoesSuportadas] = PoliticaDeVersao.VersoesSuportadasComoTexto;
        return await consultas.ObterDetalheAsync(id, cancellationToken) is { } pedido
            ? TypedResults.Ok(AdaptadorDePedidoV2.Converter(pedido))
            : TypedResults.NotFound();
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
