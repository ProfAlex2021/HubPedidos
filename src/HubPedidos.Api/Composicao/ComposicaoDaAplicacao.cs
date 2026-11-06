using HubPedidos.Application.Composicao;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Infrastructure.Composicao;

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
        servicos.AddNotificacoesPorLog();

        // Prazo para drenar a fila no encerramento; depois dele o cancelamento é forçado.
        servicos.Configure<HostOptions>(opcoes =>
            opcoes.ShutdownTimeout = TimeSpan.FromSeconds(configuracao.GetValue("Fila:PrazoDeEncerramentoEmSegundos", 15)));

        return servicos;
    }
}
