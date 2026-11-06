using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Infrastructure.Persistencia.Modelos;

/// <summary>
/// Modelo relacional do pedido. Separado da entidade de domínio para que decisões de
/// armazenamento (chave numérica, centavos, colunas de endereço) não vazem para o domínio.
/// </summary>
/// <remarks>
/// - <see cref="Numero"/>: chave primária INTEGER (rowid do SQLite), usada nas junções e
///   no desempate da ordenação e do cursor. <see cref="Id"/> é o identificador público.
/// - Dinheiro em centavos (INTEGER): o SQLite não tem tipo decimal nativo, e somas,
///   comparações e ordenações sobre inteiros são traduzidas para SQL sem perda.
/// - <see cref="ValorTotalCentavos"/> é desnormalizado (os itens não mudam depois da
///   criação), permitindo filtrar e ordenar por valor sem agregar os itens.
/// </remarks>
public sealed class PedidoRegistro
{
    public long Numero { get; set; }

    public Guid Id { get; set; }

    public string ClienteId { get; set; } = string.Empty;

    public Regiao Regiao { get; set; }

    public Prioridade Prioridade { get; set; }

    public CategoriaProcessamento Categoria { get; set; }

    public string MotivoDaCategoria { get; set; } = string.Empty;

    public string Processador { get; set; } = string.Empty;

    public int PrazoEstimadoEmDias { get; set; }

    public SituacaoPedido Situacao { get; set; }

    public DateTime CriadoEmUtc { get; set; }

    /// <summary>Roteiro 4: preenchido uma única vez pelo processamento (idempotência persistida).</summary>
    public DateTime? ProcessadoEmUtc { get; set; }

    public long ValorTotalCentavos { get; set; }

    /// <summary>Tipo complexo: colunas da própria tabela (ou JSON no contexto alternativo).</summary>
    public EnderecoRegistro? Endereco { get; set; }

    public List<ItemPedidoRegistro> Itens { get; set; } = [];
}

/// <summary>Item do pedido: tabela própria, ligada pela chave numérica do pedido.</summary>
public sealed class ItemPedidoRegistro
{
    public long Id { get; set; }

    public long PedidoNumero { get; set; }

    /// <summary>Preserva a ordem original dos itens.</summary>
    public int Posicao { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantidade { get; set; }

    public long PrecoUnitarioCentavos { get; set; }
}

/// <summary>Endereço como tipo complexo do EF Core: sem chave, sem tabela, sem tracking próprio.</summary>
public sealed class EnderecoRegistro
{
    public string? Logradouro { get; set; }

    public string Cidade { get; set; } = string.Empty;

    public string Uf { get; set; } = string.Empty;

    public string Cep { get; set; } = string.Empty;
}
