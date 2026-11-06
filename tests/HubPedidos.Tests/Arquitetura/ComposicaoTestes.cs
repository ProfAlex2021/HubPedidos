using HubPedidos.Api.Composicao;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Notificacoes;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Application.Processamento;
using HubPedidos.Application.Processamento.Fila;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HubPedidos.Tests.Arquitetura;

/// <summary>Teste de inicialização: o grafo REAL do composition root é válido.</summary>
[TestClass]
public sealed class ComposicaoTestes
{
    [TestMethod]
    public async Task Composition_root_constroi_o_grafo_sem_dependencia_cativa()
    {
        await using var app = ConstruirAplicacao();

        await using var escopo = app.Services.CreateAsyncScope();
        // Tudo que os endpoints pedem pode ser resolvido.
        Assert.IsNotNull(escopo.ServiceProvider.GetRequiredService<ServicoDePedidos>());
        Assert.IsNotNull(escopo.ServiceProvider.GetRequiredService<IConsultasDePedidos>());
        Assert.IsNotNull(escopo.ServiceProvider.GetRequiredService<IRepositorioDePedidos>());
        Assert.IsNotNull(escopo.ServiceProvider.GetRequiredService<ExecucaoDoProcessamento>());
        Assert.IsNotNull(escopo.ServiceProvider.GetRequiredService<ProcessadorDeLotes>());
        Assert.IsTrue(app.Services.GetServices<IHostedService>().OfType<ProcessadorDePedidosWorker>().Any());
    }

    [TestMethod]
    public async Task Tempos_de_vida_seguem_o_estado_de_cada_servico()
    {
        await using var app = ConstruirAplicacao();
        await using var escopoA = app.Services.CreateAsyncScope();
        await using var escopoB = app.Services.CreateAsyncScope();

        // Singletons: mesma instância em qualquer escopo.
        Assert.AreSame(escopoA.ServiceProvider.GetRequiredService<FilaDeProcessamento>(), escopoB.ServiceProvider.GetRequiredService<FilaDeProcessamento>());
        Assert.AreSame(escopoA.ServiceProvider.GetRequiredService<CatalogoDeProcessadores>(), escopoB.ServiceProvider.GetRequiredService<CatalogoDeProcessadores>());
        Assert.AreSame(escopoA.ServiceProvider.GetRequiredService<INotificadorDePedidos>(), escopoB.ServiceProvider.GetRequiredService<INotificadorDePedidos>());

        // Scoped: mesma instância dentro do escopo, outra em cada escopo.
        var repositorioA = escopoA.ServiceProvider.GetRequiredService<IRepositorioDePedidos>();
        Assert.AreSame(repositorioA, escopoA.ServiceProvider.GetRequiredService<IRepositorioDePedidos>());
        Assert.AreNotSame(repositorioA, escopoB.ServiceProvider.GetRequiredService<IRepositorioDePedidos>());
        Assert.AreNotSame(
            escopoA.ServiceProvider.GetRequiredService<ServicoDePedidos>(),
            escopoB.ServiceProvider.GetRequiredService<ServicoDePedidos>());
    }

    [TestMethod]
    public void Validacao_detecta_scoped_capturado_por_singleton()
    {
        // Contraprova: se alguém registrar um singleton que depende do repositório
        // (scoped), a validação de construção falha em vez de deixar o bug passar.
        var builder = CriarBuilder();
        builder.Services.AddSingleton<ServicoComDependenciaCativa>();

        var excecao = Assert.Throws<AggregateException>(() => builder.Build());

        StringAssert.Contains(excecao.ToString(), "Cannot consume scoped service");
    }

    private static WebApplication ConstruirAplicacao() => CriarBuilder().Build();

    private static WebApplicationBuilder CriarBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testes" });
        builder.Configuration["ConnectionStrings:HubPedidos"] = "Data Source=:memory:";
        ComposicaoDaAplicacao.ConfigurarServicos(builder.Services, builder.Configuration);
        builder.Host.UseDefaultServiceProvider(opcoes =>
        {
            opcoes.ValidateOnBuild = true;
            opcoes.ValidateScopes = true;
        });
        return builder;
    }

    private sealed class ServicoComDependenciaCativa(IRepositorioDePedidos repositorio)
    {
        public IRepositorioDePedidos Repositorio { get; } = repositorio;
    }
}
