using System.Collections.Concurrent;
using HubPedidos.Api.Seguranca;
using HubPedidos.Domain.Pedidos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HubPedidos.Api.TempoReal;

/// <summary>
/// Evento tipado enviado aos clientes. Só o mínimo para o cliente saber que algo mudou
/// e reconciliar pela API HTTP: nada de entidade, itens, valores ou dados do cliente.
/// </summary>
/// <param name="Sequencia">Instante da mudança em ticks: cresce com o tempo e permite descartar repetidos/antigos.</param>
public sealed record PedidoAtualizado(string PedidoId, string Situacao, long Sequencia, DateTime InstanteUtc);

/// <summary>Métodos que o servidor chama nos clientes (contrato tipado do hub).</summary>
public interface IClienteDePedidos
{
    Task PedidoAtualizado(PedidoAtualizado evento);

    Task AcessoRevogado(string motivo);
}

/// <summary>
/// Hub de acompanhamento. Toda conexão precisa estar autenticada; entrar no grupo de um
/// pedido exige autorização verificada AQUI, no servidor. O grupo é só um mecanismo de
/// entrega: estar nele não é prova de permissão.
/// </summary>
[Authorize]
public sealed class PedidosHub(AutorizacaoDePedidos autorizacao, RegistroDeAcompanhamentos acompanhamentos) : Hub<IClienteDePedidos>
{
    public const string Rota = "/hubs/pedidos";

    public static string Grupo(PedidoId id) => $"pedido:{id.Valor:N}";

    /// <summary>
    /// Passa a receber as atualizações de um pedido. Chamado de novo pelo cliente depois de
    /// cada reconexão (o identificador de conexão muda e os grupos não são restaurados).
    /// </summary>
    public async Task AcompanharPedido(string pedidoId)
    {
        if (!PedidoId.TryParse(pedidoId, out var id))
        {
            throw new HubException("Identificador de pedido inválido.");
        }

        // Mesma regra da consulta HTTP; "não encontrado" e "sem permissão" têm a mesma
        // resposta para não revelar a existência de pedidos de outros clientes.
        if (await autorizacao.ObterSePermitidoAsync(Context.User!, id, Context.ConnectionAborted) is null)
        {
            throw new HubException("Pedido não encontrado ou sem permissão.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(id), Context.ConnectionAborted);
        acompanhamentos.Registrar(Context.UserIdentifier!, Context.ConnectionId, Grupo(id));
    }

    public async Task DeixarDeAcompanhar(string pedidoId)
    {
        if (PedidoId.TryParse(pedidoId, out var id))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Grupo(id));
            acompanhamentos.Remover(Context.ConnectionId, Grupo(id));
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        acompanhamentos.RemoverConexao(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Quais grupos cada conexão de cada usuário acompanha. Necessário para REMOVER as
/// associações quando a permissão é revogada (o SignalR não lista membros de grupos).
/// </summary>
public sealed class RegistroDeAcompanhamentos
{
    private readonly ConcurrentDictionary<string, (string Usuario, ConcurrentDictionary<string, byte> Grupos)> _porConexao = new();

    public void Registrar(string usuario, string conexao, string grupo) =>
        _porConexao.GetOrAdd(conexao, _ => (usuario, new ConcurrentDictionary<string, byte>())).Grupos.TryAdd(grupo, 0);

    public void Remover(string conexao, string grupo)
    {
        if (_porConexao.TryGetValue(conexao, out var registro))
        {
            registro.Grupos.TryRemove(grupo, out _);
        }
    }

    public void RemoverConexao(string conexao) => _porConexao.TryRemove(conexao, out _);

    /// <summary>Conexões e grupos de um usuário, removidos do registro de forma atômica por conexão.</summary>
    public IReadOnlyList<(string Conexao, string[] Grupos)> RetirarDoUsuario(string usuario) =>
    [
        .. _porConexao
            .Where(par => par.Value.Usuario == usuario)
            .Select(par => _porConexao.TryRemove(par.Key, out var removido) ? (par.Key, removido.Grupos.Keys.ToArray()) : (par.Key, Array.Empty<string>())),
    ];
}

/// <summary>Revoga o acesso de um usuário e desfaz as associações do hub.</summary>
public sealed class ServicoDeRevogacao(
    RegistroDeRevogacoes revogacoes,
    RegistroDeAcompanhamentos acompanhamentos,
    IHubContext<PedidosHub, IClienteDePedidos> hub)
{
    public async Task<int> RevogarAsync(string usuario, CancellationToken cancellationToken)
    {
        revogacoes.Revogar(usuario);
        var removidas = 0;
        foreach (var (conexao, grupos) in acompanhamentos.RetirarDoUsuario(usuario))
        {
            foreach (var grupo in grupos)
            {
                await hub.Groups.RemoveFromGroupAsync(conexao, grupo, cancellationToken);
                removidas++;
            }

            await hub.Clients.Client(conexao).AcessoRevogado("Seu acesso foi revogado.");
        }

        return removidas;
    }
}
