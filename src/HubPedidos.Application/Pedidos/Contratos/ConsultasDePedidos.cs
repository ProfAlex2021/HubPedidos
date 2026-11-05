namespace HubPedidos.Application.Pedidos.Contratos;

/// <summary>
/// Linha do painel de pedidos: somente o que a tela exibe. É projetada direto no
/// banco; nenhuma entidade é materializada para montá-la.
/// </summary>
public sealed record PedidoResumo(
    string Id,
    string ClienteId,
    DateTime CriadoEmUtc,
    string Categoria,
    string Situacao,
    int QuantidadeDeItens,
    decimal ValorTotal);

/// <summary>Página por deslocamento (número da página + total).</summary>
public sealed record PaginaDeResultados<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoDaPagina, int Total)
{
    public int TotalDePaginas => TamanhoDaPagina == 0 ? 0 : (int)Math.Ceiling(Total / (double)TamanhoDaPagina);
}

/// <summary>Página por cursor (extensão): o cliente envia o cursor da última página.</summary>
public sealed record PaginaPorCursor<T>(IReadOnlyList<T> Itens, string? ProximoCursor);

/// <summary>Filtros do painel. Todos opcionais; datas em UTC.</summary>
public sealed record FiltroDePedidos(
    DateTime? CriadoDesdeUtc = null,
    DateTime? CriadoAteUtc = null,
    string? Categoria = null,
    decimal? ValorMinimo = null,
    string? ClienteId = null);
