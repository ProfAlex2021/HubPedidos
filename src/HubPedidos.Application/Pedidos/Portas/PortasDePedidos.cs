using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Pedidos.Portas;

// Roteiro 6: as interfaces ficam na fronteira que as CONSOME (a aplicação). A
// infraestrutura as implementa, então a dependência aponta de fora para dentro:
// Api -> Application <- Infrastructure. Cada interface expõe só as operações que os
// casos de uso realmente usam; não há "repositório genérico".

/// <summary>Resultado de uma tentativa de cancelamento.</summary>
public enum ResultadoDoCancelamento
{
    Cancelado,
    NaoEncontrado,
    SituacaoNaoPermite,
}

/// <summary>Como o relatório carrega pedidos e itens.</summary>
public enum EstrategiaDeCarregamento
{
    /// <summary>Projeção com coleção aninhada: um comando, só as colunas usadas.</summary>
    Projecao,

    /// <summary>Entidades completas com Include + AsNoTracking: um comando, todas as colunas.</summary>
    Include,

    /// <summary>Projeção com AsSplitQuery: dois comandos, sem duplicar o cabeçalho por item.</summary>
    Dividida,
}

/// <summary>Escrita de pedidos usada pelos casos de uso de pedidos e de processamento.</summary>
public interface IRepositorioDePedidos
{
    Task AdicionarAsync(Pedido pedido, ResultadoClassificacao classificacao, ResultadoProcessamento processamento, CancellationToken cancellationToken);

    Task<ResultadoDoCancelamento> CancelarAsync(PedidoId id, CancellationToken cancellationToken);

    Task<bool> ExisteAsync(PedidoId id, CancellationToken cancellationToken);

    /// <summary>Pedidos ainda "Recebido", em ordem de chegada (para reenvio à fila).</summary>
    Task<IReadOnlyList<PedidoId>> ListarPendentesAsync(int limite, CancellationToken cancellationToken);
}

/// <summary>Leituras do painel e do detalhe. Implementações não alteram dados.</summary>
public interface IConsultasDePedidos
{
    Task<PaginaDeResultados<PedidoResumo>> ListarResumosAsync(FiltroDePedidos filtro, int pagina, int tamanhoDaPagina, CancellationToken cancellationToken);

    Task<PaginaPorCursor<PedidoResumo>> ListarResumosPorCursorAsync(FiltroDePedidos filtro, string? cursor, int tamanhoDaPagina, CancellationToken cancellationToken);

    Task<PedidoResponse?> ObterDetalheAsync(PedidoId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PedidoResponse>> ListarRelatorioAsync(int pagina, int tamanhoDaPagina, EstrategiaDeCarregamento estrategia, CancellationToken cancellationToken);

    /// <summary>Roteiro 7: totais agregados por categoria (sem dados de clientes).</summary>
    Task<IReadOnlyList<EstatisticaPorCategoria>> ContarPorCategoriaAsync(DateTime desdeUtc, CancellationToken cancellationToken);
}

/// <summary>Limites de paginação, parte do contrato das consultas.</summary>
public static class LimitesDeConsulta
{
    public const int TamanhoMaximoDaPagina = 100;
}
