using HubPedidos.Application.Importacao;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Processamento;
using HubPedidos.Application.Processamento.Fila;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Application.Composicao;

/// <summary>
/// Registros de cada módulo da aplicação (Roteiro 6). O composition root (API) só
/// chama estas extensões; a escolha de implementações concretas de infraestrutura
/// fica nas extensões da Infrastructure.
/// </summary>
/// <remarks>
/// Tempos de vida:
/// - singleton: estado compartilhado e seguro para várias threads ou imutável depois de
///   construído (catálogo, fila, métricas, limitador, opções, relógio, importador);
/// - scoped: depende de algo scoped (repositório/DbContext) ou representa uma operação
///   (ServicoDePedidos por requisição, ExecucaoDoProcessamento por item da fila);
/// - nenhum transient: não há serviço barato e sem estado que precise de instância nova
///   a cada resolução.
/// </remarks>
public static class ModulosDaAplicacao
{
    /// <summary>Módulo de pedidos: recepção, classificação, criação, cancelamento e lotes.</summary>
    public static IServiceCollection AddModuloPedidos(this IServiceCollection servicos, OpcoesDeImportacao? opcoesDeImportacao = null)
    {
        var opcoes = opcoesDeImportacao ?? OpcoesDeImportacao.Padrao;
        opcoes.Validar();

        servicos.AddSingleton(TimeProvider.System);
        // A reflexão do catálogo acontece uma vez, na construção do singleton.
        servicos.AddSingleton(_ => CatalogoDeProcessadores.DescobrirPadrao());
        servicos.AddSingleton(provedor => new ServicoDeRecepcaoDePedidos(provedor.GetRequiredService<CatalogoDeProcessadores>()));
        servicos.AddScoped<ServicoDePedidos>();

        servicos.AddSingleton(opcoes);
        servicos.AddSingleton(provedor => new ImportadorDeLotesPorFluxo(provedor.GetRequiredService<OpcoesDeImportacao>()));
        servicos.AddSingleton<ProcessadorDeLotes>();
        return servicos;
    }

    /// <summary>Módulo de processamento: fila limitada, worker, limitador e métricas.</summary>
    public static IServiceCollection AddModuloProcessamento(this IServiceCollection servicos, OpcoesDaFila? opcoesDaFila = null)
    {
        var opcoes = opcoesDaFila ?? new OpcoesDaFila();
        opcoes.Validar();

        servicos.AddSingleton(opcoes);
        servicos.AddSingleton<MetricasDoProcessamento>();
        servicos.AddSingleton<FilaDeProcessamento>();
        servicos.AddSingleton(_ => new LimitadorDeAcessoAoBanco(opcoes.LimiteDeAcessoAoBanco));
        servicos.AddScoped<ExecucaoDoProcessamento>();
        servicos.AddHostedService<ProcessadorDePedidosWorker>();
        return servicos;
    }
}
