using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>
/// Interceptor que registra o texto de cada comando enviado ao banco (sem valores de
/// parâmetros). Permite associar o SQL à consulta LINQ e contar roundtrips (N+1).
/// </summary>
public sealed class ContadorDeComandos : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _comandos = new();

    public IReadOnlyList<string> Comandos => [.. _comandos];

    public int Quantidade => _comandos.Count;

    public void Limpar() => _comandos.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _comandos.Enqueue(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        _comandos.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        _comandos.Enqueue(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        _comandos.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        _comandos.Enqueue(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        _comandos.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
