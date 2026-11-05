using HubPedidos.Application.Importacao;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Consultas;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HubPedidos.Api.Endpoints;

/// <summary>Endpoints de pedidos (Minimal APIs).</summary>
public static class PedidosEndpoints
{
    public static IEndpointRouteBuilder MapPedidosEndpoints(this IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/api/pedidos", ReceberPedidoAsync)
            .WithName("ReceberPedido");

        // Roteiro 2: o corpo é lido como stream (sem binding para string) e a resposta
        // é escrita progressivamente em NDJSON.
        rotas.MapPost("/api/pedidos/lotes", ImportarLoteAsync)
            .WithName("ImportarLote")
            .Accepts<string>("text/plain")
            .Produces(StatusCodes.Status200OK, contentType: "application/x-ndjson");

        // Roteiro 3: consultas e atualização persistidas.
        rotas.MapGet("/api/pedidos", ListarAsync).WithName("ListarPedidos");
        rotas.MapGet("/api/pedidos/cursor", ListarPorCursorAsync).WithName("ListarPedidosPorCursor");
        rotas.MapGet("/api/pedidos/{id}", ObterAsync).WithName("ObterPedido");
        rotas.MapPost("/api/pedidos/{id}/cancelamento", CancelarAsync).WithName("CancelarPedido");
        rotas.MapGet("/api/relatorios/pedidos", RelatorioAsync).WithName("RelatorioDePedidos");

        return rotas;
    }

    /// <summary>
    /// Recebe o DTO, delega ao caso de uso, persiste o pedido aceito e traduz o
    /// resultado para HTTP. Nenhum tipo do domínio aparece na resposta.
    /// </summary>
    public static async Task<Results<Created<PedidoResponse>, ValidationProblem, ProblemHttpResult>> ReceberPedidoAsync(
        CriarPedidoRequest? requisicao,
        ServicoDeRecepcaoDePedidos servico,
        RepositorioDePedidos repositorio,
        CancellationToken cancellationToken)
    {
        switch (servico.Receber(requisicao))
        {
            case ResultadoDaRecepcao.Aceito aceito:
                await repositorio.AdicionarAsync(aceito.Entidade, aceito.Classificacao, aceito.Processamento, cancellationToken);
                return TypedResults.Created($"/api/pedidos/{aceito.Pedido.Id}", aceito.Pedido);

            case ResultadoDaRecepcao.DadosInvalidos invalidos:
                return TypedResults.ValidationProblem(
                    invalidos.Erros
                        .GroupBy(erro => erro.Campo)
                        .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(erro => erro.Mensagem).ToArray()));

            case ResultadoDaRecepcao.Rejeitado rejeitado:
                return TypedResults.Problem(
                    rejeitado.Motivo,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Pedido não classificável");

            default:
                throw new InvalidOperationException("Resultado de recepção desconhecido.");
        }
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

    /// <summary>Painel: filtros, ordenação determinística, paginação e total.</summary>
    public static async Task<Results<Ok<PaginaDeResultados<PedidoResumo>>, ValidationProblem>> ListarAsync(
        [AsParameters] ParametrosDoPainel parametros,
        ConsultasDePedidos consultas,
        CancellationToken cancellationToken)
    {
        if (parametros.Validar() is { } erros)
        {
            return TypedResults.ValidationProblem(erros);
        }

        return TypedResults.Ok(await consultas.ListarResumosAsync(
            parametros.ParaFiltro(), parametros.Pagina ?? 1, parametros.Tamanho ?? 20, cancellationToken));
    }

    /// <summary>Extensão: mesma listagem, paginada por cursor.</summary>
    public static async Task<Results<Ok<PaginaPorCursor<PedidoResumo>>, ValidationProblem>> ListarPorCursorAsync(
        [AsParameters] ParametrosDoPainel parametros,
        [FromQuery] string? apos,
        ConsultasDePedidos consultas,
        CancellationToken cancellationToken)
    {
        if (parametros.Validar() is { } erros)
        {
            return TypedResults.ValidationProblem(erros);
        }

        try
        {
            return TypedResults.Ok(await consultas.ListarResumosPorCursorAsync(
                parametros.ParaFiltro(), apos, parametros.Tamanho ?? 20, cancellationToken));
        }
        catch (ArgumentException excecao)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["apos"] = [excecao.Message] });
        }
    }

    /// <summary>
    /// Detalhe com itens em uma consulta. O <see cref="PedidoId"/> é convertido pelo
    /// <c>TryParse</c> do value object (Roteiro 1): identificador inválido vira 400.
    /// Usa a consulta compilada: medida 0,72× o tempo e 0,81× a alocação da comum
    /// (docs/roteiros/roteiro-03.md).
    /// </summary>
    public static async Task<Results<Ok<PedidoResponse>, NotFound>> ObterAsync(
        PedidoId id, ConsultasDePedidos consultas) =>
        await consultas.ObterDetalheCompiladoAsync(id) is { } pedido
            ? TypedResults.Ok(pedido)
            : TypedResults.NotFound();

    /// <summary>Atualização com tracking pelo mesmo contexto (ver RepositorioDePedidos).</summary>
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> CancelarAsync(
        PedidoId id, RepositorioDePedidos repositorio, CancellationToken cancellationToken) =>
        await repositorio.CancelarAsync(id, cancellationToken) switch
        {
            ResultadoDoCancelamento.Cancelado => TypedResults.NoContent(),
            ResultadoDoCancelamento.NaoEncontrado => TypedResults.NotFound(),
            _ => TypedResults.Problem("A situação atual do pedido não permite cancelamento.", statusCode: StatusCodes.Status409Conflict),
        };

    /// <summary>Relatório de pedidos com itens (sem N+1) com a estratégia de carregamento escolhida.</summary>
    public static async Task<Results<Ok<IReadOnlyList<PedidoResponse>>, ValidationProblem>> RelatorioAsync(
        ConsultasDePedidos consultas,
        CancellationToken cancellationToken,
        int pagina = 1,
        int tamanho = 20,
        EstrategiaDeCarregamento estrategia = EstrategiaDeCarregamento.Projecao)
    {
        if (pagina < 1 || tamanho is < 1 or > ConsultasDePedidos.TamanhoMaximoDaPagina)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagina"] = [$"Página >= 1 e tamanho entre 1 e {ConsultasDePedidos.TamanhoMaximoDaPagina}."],
            });
        }

        return TypedResults.Ok(await consultas.ListarRelatorioAsync(pagina, tamanho, estrategia, cancellationToken));
    }
}

/// <summary>Parâmetros de query do painel, validados antes de chegar à consulta.</summary>
public sealed record ParametrosDoPainel(
    [property: FromQuery] DateTime? Desde,
    [property: FromQuery] DateTime? Ate,
    [property: FromQuery] string? Categoria,
    [property: FromQuery] decimal? ValorMinimo,
    [property: FromQuery] string? ClienteId,
    [property: FromQuery] int? Pagina,
    [property: FromQuery] int? Tamanho)
{
    public Dictionary<string, string[]>? Validar()
    {
        var erros = new Dictionary<string, string[]>();
        if (Pagina is < 1)
        {
            erros["pagina"] = ["A página deve ser maior ou igual a 1."];
        }

        if (Tamanho is < 1 or > ConsultasDePedidos.TamanhoMaximoDaPagina)
        {
            erros["tamanho"] = [$"O tamanho deve estar entre 1 e {ConsultasDePedidos.TamanhoMaximoDaPagina}."];
        }

        if (Categoria is not null && (!Enum.TryParse<CategoriaProcessamento>(Categoria, true, out var c) || !Enum.IsDefined(c)))
        {
            erros["categoria"] = [$"Valores aceitos: {string.Join(", ", Enum.GetNames<CategoriaProcessamento>())}."];
        }

        if (ValorMinimo is < 0)
        {
            erros["valorMinimo"] = ["O valor mínimo não pode ser negativo."];
        }

        return erros.Count == 0 ? null : erros;
    }

    public FiltroDePedidos ParaFiltro() => new(
        Desde?.ToUniversalTime(), Ate?.ToUniversalTime(), Categoria, ValorMinimo, ClienteId);
}
