using HubPedidos.Application.Notificacoes;
using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Infrastructure.Consultas;
using HubPedidos.Infrastructure.Notificacoes;
using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Infrastructure.Processamento;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Infrastructure.Composicao;

/// <summary>Associa as portas da aplicação às implementações de infraestrutura.</summary>
public static class InfraestruturaExtensions
{
    /// <summary>
    /// Persistência (EF Core + SQLite). Tudo scoped: o DbContext não é seguro para uso
    /// concorrente e representa uma unidade de trabalho por requisição ou por item.
    /// </summary>
    public static IServiceCollection AddPersistenciaDePedidos(this IServiceCollection servicos, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        servicos.AddDbContext<ContextoDePedidos>(opcoes => opcoes.UseSqlite(connectionString));
        servicos.AddScoped<IRepositorioDePedidos, RepositorioDePedidos>();
        servicos.AddScoped<IConsultasDePedidos, ConsultasDePedidos>();
        servicos.AddScoped<IProcessamentoDePedido, ProcessamentoDePedidoEfCore>();
        return servicos;
    }

    /// <summary>Módulo de notificações por log. Singleton: não guarda estado.</summary>
    public static IServiceCollection AddNotificacoesPorLog(this IServiceCollection servicos)
    {
        servicos.AddSingleton<INotificadorDePedidos, NotificadorPorLog>();
        return servicos;
    }
}
