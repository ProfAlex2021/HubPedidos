using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia.Modelos;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>Conversões explícitas entre o domínio e os modelos relacionais.</summary>
internal static class MapeadorDePersistencia
{
    public static PedidoRegistro ParaRegistro(Pedido pedido, ResultadoClassificacao classificacao, ResultadoProcessamento processamento) => new()
    {
        Id = pedido.Id.Valor,
        ClienteId = pedido.ClienteId,
        Regiao = pedido.Regiao,
        Prioridade = pedido.Prioridade,
        Categoria = classificacao.Categoria,
        MotivoDaCategoria = classificacao.Motivo,
        Processador = $"{processamento.Processador} v{processamento.Versao}",
        PrazoEstimadoEmDias = processamento.PrazoEstimadoEmDias,
        Situacao = pedido.Situacao,
        CriadoEmUtc = pedido.CriadoEmUtc,
        ValorTotalCentavos = Dinheiro.ParaCentavos(pedido.ValorTotal),
        Endereco = pedido.Endereco is { } e
            ? new EnderecoRegistro { Logradouro = e.Logradouro, Cidade = e.Cidade, Uf = e.Uf, Cep = e.Cep }
            : null,
        Itens =
        [
            .. pedido.Itens.Select((item, indice) => new ItemPedidoRegistro
            {
                Posicao = indice,
                Sku = item.Sku,
                Quantidade = item.Quantidade,
                PrecoUnitarioCentavos = Dinheiro.ParaCentavos(item.PrecoUnitario),
            }),
        ],
    };

    /// <summary>Reconstitui a entidade (passando pelas invariantes do domínio).</summary>
    public static Pedido ParaDominio(PedidoRegistro registro) => Pedido.Reconstituir(
        new PedidoId(registro.Id),
        registro.ClienteId,
        registro.Regiao,
        registro.Prioridade,
        registro.Itens
            .OrderBy(i => i.Posicao)
            .Select(i => new ItemPedido(i.Sku, i.Quantidade, Dinheiro.DeCentavos(i.PrecoUnitarioCentavos))),
        registro.Endereco is { } e ? new EnderecoEntrega(e.Logradouro, e.Cidade, e.Uf, e.Cep) : null,
        registro.CriadoEmUtc,
        registro.Situacao);
}

/// <summary>Conversão exata entre reais (2 casas, garantidas pelo domínio) e centavos.</summary>
public static class Dinheiro
{
    public static long ParaCentavos(decimal valor) => decimal.ToInt64(decimal.Round(valor * 100m, 0, MidpointRounding.ToEven));

    public static decimal DeCentavos(long centavos) => centavos / 100m;
}
