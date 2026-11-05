using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento.Processadores;

// Estratégias de processamento. Todas são classes seladas, sem estado e com
// construtor público sem parâmetros: o catálogo cria uma instância de cada
// na inicialização e reutiliza essa instância em todas as chamadas.

/// <summary>Pedido comum: separação no fluxo normal.</summary>
[ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
public sealed class ProcessadorPadrao : IProcessadorDePedido
{
    public ResultadoProcessamento Processar(Pedido pedido) =>
        new(nameof(ProcessadorPadrao), 1, PrazoEstimadoEmDias: 5, "Separação no fluxo normal.");
}

/// <summary>Prioridade alta/urgente fora das condições da entrega expressa.</summary>
[ProcessadorPedido(CategoriaProcessamento.Prioritaria, versao: 1)]
public sealed class ProcessadorPrioritario : IProcessadorDePedido
{
    public ResultadoProcessamento Processar(Pedido pedido) =>
        new(nameof(ProcessadorPrioritario), 1, PrazoEstimadoEmDias: 2, "Separação antecipada na fila prioritária.");
}

/// <summary>Primeira versão da entrega expressa (mantida para mostrar o versionamento).</summary>
[ProcessadorPedido(CategoriaProcessamento.Expressa, versao: 1)]
public sealed class ProcessadorExpresso : IProcessadorDePedido
{
    public ResultadoProcessamento Processar(Pedido pedido) =>
        new(nameof(ProcessadorExpresso), 1, PrazoEstimadoEmDias: 1, "Entrega expressa com coleta no mesmo dia.");
}

/// <summary>
/// Segunda versão da entrega expressa. Como o catálogo escolhe a maior versão de
/// cada categoria, esta substitui a v1 sem editar nenhuma lista central.
/// </summary>
[ProcessadorPedido(CategoriaProcessamento.Expressa, versao: 2)]
public sealed class ProcessadorExpressoV2 : IProcessadorDePedido
{
    public ResultadoProcessamento Processar(Pedido pedido) =>
        new(nameof(ProcessadorExpressoV2), 2, PrazoEstimadoEmDias: 1,
            $"Entrega expressa consolidada em uma remessa para a região {pedido.Regiao}.");
}

/// <summary>Muitas unidades: divide o pedido em remessas.</summary>
[ProcessadorPedido(CategoriaProcessamento.Volumosa, versao: 1)]
public sealed class ProcessadorVolumoso : IProcessadorDePedido
{
    private const int UnidadesPorRemessa = 50;

    public ResultadoProcessamento Processar(Pedido pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var remessas = (pedido.QuantidadeDeItens + UnidadesPorRemessa - 1) / UnidadesPorRemessa;
        return new(nameof(ProcessadorVolumoso), 1, PrazoEstimadoEmDias: 7,
            $"Pedido dividido em {remessas} remessas de até {UnidadesPorRemessa} unidades.");
    }
}

/// <summary>Valor alto: exige revisão manual antes da separação.</summary>
[ProcessadorPedido(CategoriaProcessamento.AltoValor, versao: 1)]
public sealed class ProcessadorAltoValor : IProcessadorDePedido
{
    public ResultadoProcessamento Processar(Pedido pedido) =>
        new(nameof(ProcessadorAltoValor), 1, PrazoEstimadoEmDias: 6, "Aguardando revisão manual por valor elevado.");
}
