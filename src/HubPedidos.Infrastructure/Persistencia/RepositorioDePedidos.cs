using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>
/// Escrita de pedidos com EF Core; implementa a porta <see cref="IRepositorioDePedidos"/>
/// declarada pela aplicação. Recebe o contexto por injeção (tempo de vida "scoped": um
/// contexto por requisição, nunca compartilhado entre threads).
/// </summary>
public sealed class RepositorioDePedidos(ContextoDePedidos contexto) : IRepositorioDePedidos
{
    public Task<bool> ExisteAsync(PedidoId id, CancellationToken cancellationToken) =>
        contexto.Pedidos.AnyAsync(p => p.Id == id.Valor, cancellationToken);

    public async Task<IReadOnlyList<PedidoId>> ListarPendentesAsync(int limite, CancellationToken cancellationToken)
    {
        var ids = await contexto.Pedidos.AsNoTracking()
            .Where(p => p.Situacao == SituacaoPedido.Recebido)
            .OrderBy(p => p.Numero)
            .Select(p => p.Id)
            .Take(limite)
            .ToListAsync(cancellationToken);
        return [.. ids.Select(id => new PedidoId(id))];
    }

    public async Task AdicionarAsync(
        Pedido pedido,
        ResultadoClassificacao classificacao,
        ResultadoProcessamento processamento,
        CancellationToken cancellationToken = default)
    {
        contexto.Pedidos.Add(MapeadorDePersistencia.ParaRegistro(pedido, classificacao, processamento));
        await contexto.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Estratégia principal: consulta COM tracking, porque a entidade será alterada e
    /// salva pelo mesmo contexto. O domínio decide se a transição é válida; o EF detecta
    /// que só a coluna Situacao mudou e gera um UPDATE apenas dela.
    /// </summary>
    public async Task<ResultadoDoCancelamento> CancelarAsync(PedidoId id, CancellationToken cancellationToken = default)
    {
        var registro = await contexto.Pedidos
            .Include(p => p.Itens) // necessários para reconstituir o domínio e validar a regra
            .SingleOrDefaultAsync(p => p.Id == id.Valor, cancellationToken);
        if (registro is null)
        {
            return ResultadoDoCancelamento.NaoEncontrado;
        }

        var pedido = MapeadorDePersistencia.ParaDominio(registro);
        try
        {
            pedido.Cancelar();
        }
        catch (InvalidOperationException)
        {
            return ResultadoDoCancelamento.SituacaoNaoPermite;
        }

        registro.Situacao = pedido.Situacao; // altera a instância rastreada
        await contexto.SaveChangesAsync(cancellationToken);
        return ResultadoDoCancelamento.Cancelado;
    }

    /// <summary>
    /// Estratégia alternativa SEM tracking, documentada e testada: um único UPDATE com
    /// a regra traduzida para a cláusula WHERE. Não materializa entidades, mas a regra
    /// do domínio é duplicada na consulta. Mantida para comparação, não usada pela API.
    /// </summary>
    public async Task<ResultadoDoCancelamento> CancelarSemTrackingAsync(PedidoId id, CancellationToken cancellationToken = default)
    {
        var afetados = await contexto.Pedidos
            .Where(p => p.Id == id.Valor && p.Situacao == SituacaoPedido.Recebido)
            .ExecuteUpdateAsync(atualizacao => atualizacao.SetProperty(p => p.Situacao, SituacaoPedido.Cancelado), cancellationToken);

        if (afetados == 1)
        {
            return ResultadoDoCancelamento.Cancelado;
        }

        var existe = await contexto.Pedidos.AnyAsync(p => p.Id == id.Valor, cancellationToken);
        return existe ? ResultadoDoCancelamento.SituacaoNaoPermite : ResultadoDoCancelamento.NaoEncontrado;
    }
}
