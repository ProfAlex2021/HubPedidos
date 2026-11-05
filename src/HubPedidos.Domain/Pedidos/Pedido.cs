using System.Collections.ObjectModel;

namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Entidade Pedido. Permanece classe (e não record) porque sua identidade é o
/// <see cref="Id"/>: dois pedidos com os mesmos dados continuam sendo pedidos diferentes.
/// </summary>
/// <remarks>
/// O construtor é a fronteira que impede identificadores, quantidades e preços
/// inválidos. Neste roteiro o pedido não muda de estado depois de criado.
/// </remarks>
public sealed class Pedido
{
    public const int TamanhoMaximoClienteId = 50;
    public const int MaximoDeLinhas = 200;

    private readonly ReadOnlyCollection<ItemPedido> _itens;

    public Pedido(PedidoId id, string clienteId, Regiao regiao, Prioridade prioridade, IEnumerable<ItemPedido> itens)
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

        // O pedido é imutável neste roteiro, então os totais são calculados uma vez.
        ValorTotal = copia.Sum(item => item.Subtotal);
        QuantidadeDeItens = copia.Sum(item => item.Quantidade);
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
