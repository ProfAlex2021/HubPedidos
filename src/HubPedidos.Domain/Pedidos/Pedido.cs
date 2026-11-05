using System.Collections.ObjectModel;

namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Entidade Pedido. Permanece classe (e não record) porque sua identidade é o
/// <see cref="Id"/>: dois pedidos com os mesmos dados continuam sendo pedidos diferentes.
/// </summary>
/// <remarks>
/// O construtor é a fronteira que impede identificadores, quantidades e preços
/// inválidos. Itens e totais nunca mudam depois da criação; a única mudança de estado
/// (Roteiro 3) é a <see cref="Situacao"/>, sempre por método que protege a regra.
/// </remarks>
public sealed class Pedido
{
    public const int TamanhoMaximoClienteId = 50;
    public const int MaximoDeLinhas = 200;

    private readonly ReadOnlyCollection<ItemPedido> _itens;

    public Pedido(
        PedidoId id,
        string clienteId,
        Regiao regiao,
        Prioridade prioridade,
        IEnumerable<ItemPedido> itens,
        EnderecoEntrega? endereco = null,
        DateTime? criadoEmUtc = null)
    {
        if (!id.EhValido)
        {
            throw new ArgumentException("O identificador do pedido deve ser informado.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(clienteId) || clienteId.Trim().Length > TamanhoMaximoClienteId)
        {
            throw new ArgumentException(
                $"O cliente deve ser informado com até {TamanhoMaximoClienteId} caracteres.", nameof(clienteId));
        }

        if (!Enum.IsDefined(regiao))
        {
            throw new ArgumentOutOfRangeException(nameof(regiao), "Região desconhecida.");
        }

        if (!Enum.IsDefined(prioridade))
        {
            throw new ArgumentOutOfRangeException(nameof(prioridade), "Prioridade desconhecida.");
        }

        ArgumentNullException.ThrowIfNull(itens);

        // Cópia defensiva: a coleção recebida pertence ao chamador e pode mudar depois.
        // O array copiado fica protegido por uma ReadOnlyCollection, então nem quem
        // chamou nem quem lê Itens consegue alterar a composição do pedido.
        ItemPedido[] copia = [.. itens];
        if (copia.Length is 0 or > MaximoDeLinhas)
        {
            throw new ArgumentException(
                $"O pedido deve ter entre 1 e {MaximoDeLinhas} itens.", nameof(itens));
        }

        if (Array.Exists(copia, item => item is null))
        {
            throw new ArgumentException("A lista de itens não pode conter valores nulos.", nameof(itens));
        }

        Id = id;
        ClienteId = clienteId.Trim();
        Regiao = regiao;
        Prioridade = prioridade;
        _itens = Array.AsReadOnly(copia);
        Endereco = endereco;
        CriadoEmUtc = DateTime.SpecifyKind(criadoEmUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
        Situacao = SituacaoPedido.Recebido;

        // Os itens não mudam depois da criação, então os totais são calculados uma vez.
        ValorTotal = copia.Sum(item => item.Subtotal);
        QuantidadeDeItens = copia.Sum(item => item.Quantidade);
    }

    /// <summary>
    /// Recria um pedido já persistido. Passa pelo construtor (as invariantes continuam
    /// valendo) e só então aplica a situação registrada.
    /// </summary>
    public static Pedido Reconstituir(
        PedidoId id,
        string clienteId,
        Regiao regiao,
        Prioridade prioridade,
        IEnumerable<ItemPedido> itens,
        EnderecoEntrega? endereco,
        DateTime criadoEmUtc,
        SituacaoPedido situacao)
    {
        if (!Enum.IsDefined(situacao))
        {
            throw new ArgumentOutOfRangeException(nameof(situacao), "Situação desconhecida.");
        }

        return new Pedido(id, clienteId, regiao, prioridade, itens, endereco, criadoEmUtc) { Situacao = situacao };
    }

    public EnderecoEntrega? Endereco { get; }

    public DateTime CriadoEmUtc { get; }

    public SituacaoPedido Situacao { get; private set; }

    /// <summary>Somente um pedido recebido (ainda não processado) pode ser cancelado.</summary>
    public void Cancelar()
    {
        if (Situacao != SituacaoPedido.Recebido)
        {
            throw new InvalidOperationException($"Um pedido na situação '{Situacao}' não pode ser cancelado.");
        }

        Situacao = SituacaoPedido.Cancelado;
    }

    public PedidoId Id { get; }

    public string ClienteId { get; }

    public Regiao Regiao { get; }

    public Prioridade Prioridade { get; }

    public IReadOnlyList<ItemPedido> Itens => _itens;

    public decimal ValorTotal { get; }

    /// <summary>Soma das quantidades de todas as linhas (unidades).</summary>
    public int QuantidadeDeItens { get; }

    /// <summary>Dados mínimos exigidos pela classificação.</summary>
    public PerfilDeClassificacao ObterPerfil() => new(ValorTotal, QuantidadeDeItens, Regiao, Prioridade);
}
