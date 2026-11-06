using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Pedidos;

/// <summary>Resultado do caso de uso, representado por uma pequena hierarquia fechada.</summary>
public abstract record ResultadoDaRecepcao
{
    private ResultadoDaRecepcao()
    {
    }

    /// <summary>
    /// Pedido válido, classificado e processado. Além do DTO, carrega as decisões
    /// tomadas (Roteiro 3) para que a camada de persistência possa gravá-las.
    /// </summary>
    public sealed record Aceito(
        PedidoResponse Pedido,
        Domain.Pedidos.Pedido Entidade,
        ResultadoClassificacao Classificacao,
        ResultadoProcessamento Processamento) : ResultadoDaRecepcao;

    /// <summary>O DTO foi rejeitado antes de qualquer objeto de domínio ser criado.</summary>
    public sealed record DadosInvalidos(IReadOnlyList<ErroDeValidacao> Erros) : ResultadoDaRecepcao;

    /// <summary>O domínio foi criado, mas a classificação não encontrou categoria válida.</summary>
    public sealed record Rejeitado(string Motivo) : ResultadoDaRecepcao;
}

/// <summary>
/// Caso de uso do Roteiro 1: recebe o DTO, converte para domínio, classifica,
/// seleciona o processador no catálogo e devolve uma resposta tipada.
/// </summary>
public sealed class ServicoDeRecepcaoDePedidos(CatalogoDeProcessadores catalogo, Func<PedidoId>? geradorDeId = null)
{
    private readonly Func<PedidoId> _geradorDeId = geradorDeId ?? PedidoId.Novo;

    public ResultadoDaRecepcao Receber(CriarPedidoRequest? requisicao)
    {
        var mapeamento = MapeadorDePedido.ParaDominio(requisicao, _geradorDeId());
        if (!mapeamento.Sucesso)
        {
            return new ResultadoDaRecepcao.DadosInvalidos(mapeamento.Erros);
        }

        var pedido = mapeamento.Valor;
        var classificacao = ClassificadorDePedidos.Classificar(pedido);

        // Um Pedido válido sempre recebe categoria válida; a verificação explícita
        // protege o fluxo se a regra de classificação mudar no futuro.
        if (!classificacao.EhValida || !catalogo.TentarObter(classificacao.Categoria, out var entrada))
        {
            return new ResultadoDaRecepcao.Rejeitado(classificacao.Motivo);
        }

        var processamento = entrada.Instancia.Processar(pedido);
        Diagnostico.TelemetriaDoHub.PedidosRecebidos.Add(1, new KeyValuePair<string, object?>("categoria", classificacao.Categoria.ToString()));
        return new ResultadoDaRecepcao.Aceito(
            MapeadorDePedido.ParaResposta(pedido, classificacao, processamento),
            pedido,
            classificacao,
            processamento);
    }
}
