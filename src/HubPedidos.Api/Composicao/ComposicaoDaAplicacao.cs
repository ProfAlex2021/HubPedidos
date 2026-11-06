using HubPedidos.Api.Endpoints;
using HubPedidos.Api.Seguranca;
using HubPedidos.Api.TempoReal;
using HubPedidos.Application.Composicao;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Notificacoes;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Infrastructure.Composicao;
using Microsoft.AspNetCore.Authentication.BearerToken;

namespace HubPedidos.Api.Composicao;

/// <summary>
/// Composition root (Roteiro 6): o ÚNICO lugar que conhece ao mesmo tempo as portas da
/// aplicação e as implementações da infraestrutura. Fica separado do Program para que
/// o teste de inicialização valide exatamente o mesmo grafo que a aplicação usa.
/// </summary>
public static class ComposicaoDaAplicacao
{
    public static IServiceCollection ConfigurarServicos(IServiceCollection servicos, IConfiguration configuracao)
    {
        // A connection string NÃO está no appsettings: vem de User Secrets (desenvolvimento)
        // ou da variável de ambiente ConnectionStrings__HubPedidos. Sem ela, a aplicação não sobe.
        var connectionString = configuracao.GetConnectionString("HubPedidos")
            ?? throw new InvalidOperationException(
                "Connection string 'HubPedidos' não configurada. Execute scripts/configurar-desenvolvimento.ps1 " +
                "ou defina a variável de ambiente ConnectionStrings__HubPedidos.");

        servicos.AddProblemDetails();

        // Um documento OpenAPI por versão: /openapi/v1.json e /openapi/v2.json.
        servicos.AddOpenApi("v1", opcoes => opcoes.ShouldInclude = descricao => descricao.GroupName == "v1");
        servicos.AddOpenApi("v2", opcoes => opcoes.ShouldInclude = descricao => descricao.GroupName == "v2");

        // Módulos da aplicação.
        servicos.AddModuloPedidos(configuracao.GetSection("Importacao").Get<OpcoesDeImportacao>());
        servicos.AddModuloProcessamento(configuracao.GetSection("Fila").Get<OpcoesDaFila>());

        // Implementações de infraestrutura para as portas.
        servicos.AddPersistenciaDePedidos(connectionString);

        // Roteiro 7: notificações em tempo real substituem as notificações por log
        // (troca de implementação no composition root; quem publica não muda).
        servicos.AddTempoRealECache();

        // Prazo para drenar a fila no encerramento; depois dele o cancelamento é forçado.
        servicos.Configure<HostOptions>(opcoes =>
            opcoes.ShutdownTimeout = TimeSpan.FromSeconds(configuracao.GetValue("Fila:PrazoDeEncerramentoEmSegundos", 15)));

        return servicos;
    }

    /// <summary>Autenticação, autorização, SignalR, output cache e notificador da API.</summary>
    private static IServiceCollection AddTempoRealECache(this IServiceCollection servicos)
    {
        servicos.AddAuthentication(BearerTokenDefaults.AuthenticationScheme)
            .AddBearerToken(opcoes =>
            {
                opcoes.BearerTokenExpiration = TimeSpan.FromHours(1);
                // WebSockets e Server-Sent Events não permitem cabeçalhos no navegador: o
                // cliente SignalR envia o token na query string, aceito SOMENTE na rota do hub.
                opcoes.Events = new BearerTokenEvents
                {
                    OnMessageReceived = contexto =>
                    {
                        var token = contexto.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && contexto.HttpContext.Request.Path.StartsWithSegments(PedidosHub.Rota))
                        {
                            contexto.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        servicos.AddAuthorization();

        servicos.AddSingleton<RegistroDeRevogacoes>();
        servicos.AddSingleton<ServicoDeAutenticacao>();
        servicos.AddScoped<AutorizacaoDePedidos>();

        servicos.AddSignalR();
        servicos.AddSingleton<RegistroDeAcompanhamentos>();
        servicos.AddSingleton<ServicoDeRevogacao>();

        servicos.AddOutputCache(opcoes =>
        {
            // Política explícita, aplicada só às estatísticas públicas: 30 s, uma entrada
            // por valor de "dias", invalidada pela tag "pedidos" quando um pedido muda.
            opcoes.AddPolicy(TempoRealEndpoints.PoliticaDeCacheDasEstatisticas, politica => politica
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByQuery("dias")
                .Tag(NotificadorDaApi.TagDoCache));
        });

        servicos.AddSingleton<INotificadorDePedidos, NotificadorDaApi>();
        return servicos;
    }
}
