using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Pedidos;

/// <summary>
/// Mapeamentos explícitos entre DTOs e domínio. Cada direção declara quais dados
/// atravessam a fronteira e registra as conversões que podem falhar.
/// </summary>
public static class MapeadorDePedido
{
    /// <summary>
    /// Converte o DTO em entidade. Toda a validação acontece ANTES de criar qualquer
    /// objeto de domínio: se houver erro, nenhum <see cref="ItemPedido"/> ou
    /// <see cref="Pedido"/> é construído e todos os problemas voltam juntos.
    /// </summary>
    public static ResultadoDeMapeamento<Pedido> ParaDominio(CriarPedidoRequest? requisicao, PedidoId idGerado)
    {
        if (requisicao is null)
        {
            return ResultadoDeMapeamento<Pedido>.Falha([new("$", "O corpo da requisição deve ser informado.")]);
        }

        var erros = new List<ErroDeValidacao>();

        if (string.IsNullOrWhiteSpace(requisicao.ClienteId))
        {
            erros.Add(new("clienteId", "O cliente deve ser informado."));
        }
        else if (requisicao.ClienteId.Trim().Length > Pedido.TamanhoMaximoClienteId)
        {
            erros.Add(new("clienteId", $"O cliente deve ter no máximo {Pedido.TamanhoMaximoClienteId} caracteres."));
        }

        // Conversões que podem falhar: texto -> enumeração.
        var regiaoValida = TentarConverterEnum<Regiao>(requisicao.Regiao, "regiao", erros, out var regiao);
        var prioridadeValida = TentarConverterEnum<Prioridade>(requisicao.Prioridade, "prioridade", erros, out var prioridade);

        var itens = requisicao.Itens;
        if (itens is null || itens.Count == 0)
        {
            erros.Add(new("itens", "O pedido deve conter ao menos um item."));
        }
        else if (itens.Count > Pedido.MaximoDeLinhas)
        {
            erros.Add(new("itens", $"O pedido pode ter no máximo {Pedido.MaximoDeLinhas} itens."));
        }
        else
        {
            for (var i = 0; i < itens.Count; i++)
            {
                ValidarItem(itens[i], $"itens[{i}]", erros);
            }
        }

        if (erros.Count > 0 || !regiaoValida || !prioridadeValida)
        {
            return ResultadoDeMapeamento<Pedido>.Falha(erros);
        }

        // Daqui em diante os dados já foram validados; os construtores do domínio
        // revalidam as invariantes como última linha de defesa.
        var itensDoDominio = itens!
            .Select(item => new ItemPedido(item.Sku!, item.Quantidade, item.PrecoUnitario))
            .ToArray();

        var pedido = new Pedido(idGerado, requisicao.ClienteId!, regiao, prioridade, itensDoDominio);
        return ResultadoDeMapeamento<Pedido>.Ok(pedido);
    }

    /// <summary>
    /// Converte a entidade e as decisões tomadas sobre ela no DTO de saída.
    /// A coleção de itens é materializada em um novo array: a resposta é um snapshot.
    /// </summary>
    public static PedidoResponse ParaResposta(
        Pedido pedido,
        ResultadoClassificacao classificacao,
        ResultadoProcessamento processamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(classificacao);
        ArgumentNullException.ThrowIfNull(processamento);

        ItemPedidoResponse[] itens =
        [
            .. pedido.Itens.Select(item =>
                new ItemPedidoResponse(item.Sku, item.Quantidade, item.PrecoUnitario, item.Subtotal)),
        ];

        return new PedidoResponse
        {
            Id = pedido.Id.ToString(),
            ClienteId = pedido.ClienteId,
            Regiao = pedido.Regiao.ToString(),
            Prioridade = pedido.Prioridade.ToString(),
            Categoria = classificacao.Categoria.ToString(),
            MotivoDaCategoria = classificacao.Motivo,
            Processador = $"{processamento.Processador} v{processamento.Versao}",
            PrazoEstimadoEmDias = processamento.PrazoEstimadoEmDias,
            ValorTotal = pedido.ValorTotal,
            QuantidadeDeItens = pedido.QuantidadeDeItens,
            Itens = Array.AsReadOnly(itens),
        };
    }

    private static void ValidarItem(ItemPedidoRequest? item, string caminho, List<ErroDeValidacao> erros)
    {
        if (item is null)
        {
            erros.Add(new(caminho, "O item não pode ser nulo."));
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Sku))
        {
            erros.Add(new($"{caminho}.sku", "O SKU deve ser informado."));
        }
        else if (item.Sku.Trim().Length > ItemPedido.TamanhoMaximoSku)
        {
            erros.Add(new($"{caminho}.sku", $"O SKU deve ter no máximo {ItemPedido.TamanhoMaximoSku} caracteres."));
        }

        if (item.Quantidade is < 1 or > ItemPedido.QuantidadeMaxima)
        {
            erros.Add(new($"{caminho}.quantidade", $"A quantidade deve estar entre 1 e {ItemPedido.QuantidadeMaxima}."));
        }

        if (item.PrecoUnitario is <= 0m or > ItemPedido.PrecoMaximo)
        {
            erros.Add(new($"{caminho}.precoUnitario", $"O preço unitário deve ser maior que zero e no máximo {ItemPedido.PrecoMaximo}."));
        }
        else if (decimal.Round(item.PrecoUnitario, 2) != item.PrecoUnitario)
        {
            erros.Add(new($"{caminho}.precoUnitario", "O preço unitário deve ter no máximo duas casas decimais."));
        }
    }

    private static bool TentarConverterEnum<TEnum>(string? texto, string campo, List<ErroDeValidacao> erros, out TEnum valor)
        where TEnum : struct, Enum
    {
        valor = default;
        if (string.IsNullOrWhiteSpace(texto))
        {
            erros.Add(new(campo, $"O campo deve ser informado. Valores aceitos: {string.Join(", ", Enum.GetNames<TEnum>())}."));
            return false;
        }

        // Enum.TryParse aceita números ("5") e combinações ("Sul,Norte"); o contrato
        // aceita apenas o nome de um valor definido, sem diferenciar maiúsculas.
        var nome = texto.Trim();
        var somenteNome = nome.All(char.IsLetter);
        if (!somenteNome || !Enum.TryParse(nome, ignoreCase: true, out valor) || !Enum.IsDefined(valor))
        {
            valor = default;
            erros.Add(new(campo, $"Valor '{nome}' não reconhecido. Valores aceitos: {string.Join(", ", Enum.GetNames<TEnum>())}."));
            return false;
        }

        return true;
    }
}
