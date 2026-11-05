using HubPedidos.Application.Processamento;

namespace HubPedidos.Api.Endpoints;

/// <summary>Expõe o catálogo para inspeção: mostra o que a reflexão encontrou.</summary>
public static class ProcessadoresEndpoints
{
    public static IEndpointRouteBuilder MapProcessadoresEndpoints(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/api/processadores", (CatalogoDeProcessadores catalogo) =>
        {
            var ativos = catalogo.Ativos.Select(e => e.Tipo).ToHashSet();
            return TypedResults.Ok(new
            {
                origem = catalogo.Origem.ToString(),
                assembly = catalogo.Diagnostico?.Assembly,
                namespaceAutorizado = catalogo.Diagnostico?.NamespaceAutorizado,
                tiposNoAssembly = catalogo.Diagnostico?.TiposNoAssembly,
                tiposNoEscopo = catalogo.Diagnostico?.TiposNoEscopo.Count,
                processadores = catalogo.Entradas.Select(e => new
                {
                    categoria = e.Categoria.ToString(),
                    versao = e.Versao,
                    tipo = e.Tipo.Name,
                    ativo = ativos.Contains(e.Tipo),
                }),
            });
        })
        .WithName("ListarProcessadores");

        return rotas;
    }
}
