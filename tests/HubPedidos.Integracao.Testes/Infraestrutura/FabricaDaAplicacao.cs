using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HubPedidos.Integracao.Testes.Infraestrutura;

/// <summary>
/// Fábrica customizada (Roteiro 8): hospeda a API REAL em memória e substitui apenas o
/// banco (SQLite em memória) e, quando o teste pede, outros serviços externos.
/// </summary>
/// <remarks>
/// <para><b>Banco efêmero.</b> Cada fábrica cria um banco em memória com nome único,
/// em modo de cache compartilhado. A <i>conexão guardiã</i> é aberta no construtor e
/// fechada só no descarte: enquanto ela existir, o banco existe. Os DbContexts abrem as
/// próprias conexões para o MESMO banco.</para>
/// <para><b>Por que não uma única SqliteConnection, como na Unidade IV?</b> Aqui o host
/// tem um worker em segundo plano e o SignalR, além das requisições. Uma SqliteConnection
/// não pode ser usada por duas threads ao mesmo tempo; com uma conexão por DbContext e o
/// cache compartilhado, o uso concorrente é seguro e o banco continua em memória.</para>
/// <para><b>Sem acesso acidental ao banco de desenvolvimento.</b> (1) Ambiente "Testes":
/// os User Secrets (onde está a connection string de desenvolvimento) não são carregados.
/// (2) O registro do DbContext é refeito DEPOIS de qualquer substituição pedida pelo
/// teste, então nem uma variável de ambiente nem uma substituição o desviam.
/// (3) Ao subir o host, a fábrica confere qual banco o DbContext usa e recusa tudo que
/// não seja o banco em memória dela.</para>
/// <para><b>Estado conhecido.</b> O esquema nasce pelas migrations (as mesmas do
/// produto) e nenhum dado é semeado: cada teste cria o que precisa.</para>
/// </remarks>
public sealed class FabricaDaAplicacao : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conexaoGuardia;
    private readonly Action<IServiceCollection>? _substituicoes;
    private readonly IReadOnlyDictionary<string, string?> _configuracao;

    /// <param name="substituicoes">Troca de serviços externos (ex.: consumidor observável da fila).</param>
    /// <param name="configuracao">Configuração extra (ex.: capacidade da fila).</param>
    public FabricaDaAplicacao(
        Action<IServiceCollection>? substituicoes = null,
        IReadOnlyDictionary<string, string?>? configuracao = null)
    {
        _substituicoes = substituicoes;
        _configuracao = configuracao ?? new Dictionary<string, string?>();
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"hubpedidos-testes-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            // Sem pool: ao fechar a conexão guardiã, nenhuma conexão ociosa mantém o banco vivo.
            Pooling = false,
        }.ToString();
        GarantirBancoEfemero(ConnectionString);

        _conexaoGuardia = new SqliteConnection(ConnectionString);
        _conexaoGuardia.Open();
    }

    /// <summary>Connection string do banco em memória desta fábrica.</summary>
    public string ConnectionString { get; }

    /// <summary>Usuários: ana (cliente-001), bruno (cliente-002) e operador. Senhas novas a cada fábrica.</summary>
    public IReadOnlyDictionary<string, string> Senhas { get; } = new Dictionary<string, string>
    {
        ["ana"] = NovaSenha(),
        ["bruno"] = NovaSenha(),
        ["operador"] = NovaSenha(),
    };

    /// <summary>Recusa qualquer banco que não seja SQLite em memória.</summary>
    public static void GarantirBancoEfemero(string? connectionString)
    {
        var construtor = new SqliteConnectionStringBuilder(connectionString ?? string.Empty);
        if (construtor.Mode != SqliteOpenMode.Memory && construtor.DataSource != ":memory:")
        {
            throw new InvalidOperationException(
                $"A fábrica de testes só usa SQLite em memória; recusou o banco '{construtor.DataSource}'. " +
                "Testes nunca devem tocar o banco de desenvolvimento.");
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");

        // A configuração recebe o MESMO banco em memória: até um código que lesse a
        // connection string direto da configuração cairia no banco do teste.
        builder.UseSetting("ConnectionStrings:HubPedidos", ConnectionString);
        builder.UseSetting("Banco:AplicarMigracoesNaInicializacao", "true");
        builder.UseSetting("Banco:ModoJournal", string.Empty); // journal não se aplica a banco em memória
        builder.UseSetting("Fila:DuracaoSimuladaEmMs", "0");   // sem atraso artificial nos testes

        (string Nome, string Papel, string? Cliente)[] usuarios =
            [("ana", "cliente", "cliente-001"), ("bruno", "cliente", "cliente-002"), ("operador", "operador", null)];
        for (var i = 0; i < usuarios.Length; i++)
        {
            builder.UseSetting($"Autenticacao:Usuarios:{i}:Nome", usuarios[i].Nome);
            builder.UseSetting($"Autenticacao:Usuarios:{i}:Senha", Senhas[usuarios[i].Nome]);
            builder.UseSetting($"Autenticacao:Usuarios:{i}:Papel", usuarios[i].Papel);
            if (usuarios[i].Cliente is { } cliente)
            {
                builder.UseSetting($"Autenticacao:Usuarios:{i}:ClienteId", cliente);
            }
        }

        foreach (var (chave, valor) in _configuracao)
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureTestServices(servicos =>
        {
            // 1º as substituições do teste; 2º o banco. Assim nenhuma substituição desvia o banco.
            _substituicoes?.Invoke(servicos);

            servicos.RemoveAll<DbContextOptions<ContextoDePedidos>>();
            servicos.RemoveAll<IDbContextOptionsConfiguration<ContextoDePedidos>>();
            servicos.RemoveAll<ContextoDePedidos>();
            servicos.AddDbContext<ContextoDePedidos>(opcoes => opcoes.UseSqlite(ConnectionString));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Conferência final: o DbContext que a aplicação resolve aponta para o banco desta fábrica.
        using var escopo = host.Services.CreateScope();
        var usado = escopo.ServiceProvider.GetRequiredService<ContextoDePedidos>().Database.GetConnectionString();
        if (usado != ConnectionString)
        {
            host.Dispose();
            GarantirBancoEfemero(usado);
            throw new InvalidOperationException("O DbContext da aplicação não usa o banco em memória desta fábrica.");
        }

        return host;
    }

    /// <summary>
    /// Cliente HTTP com comportamento definido: NÃO segue redirecionamentos (o teste vê o
    /// 3xx, se houver) e não guarda cookies (a autenticação é só o bearer token explícito).
    /// </summary>
    public HttpClient CriarCliente() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public async Task<string> ObterTokenAsync(string usuario)
    {
        var resposta = await CriarCliente().PostAsJsonAsync("/api/autenticacao/token", new { usuario, senha = Senhas[usuario] });
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<RespostaDeToken>())!.AccessToken;
    }

    public async Task<HttpClient> CriarClienteAutenticadoAsync(string usuario)
    {
        var cliente = CriarCliente();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ObterTokenAsync(usuario));
        return cliente;
    }

    /// <summary>Dado mínimo da fixture: cria um pedido pela API e devolve o identificador.</summary>
    public async Task<string> CriarPedidoAsync(string cliente, decimal precoUnitario = 10m, int quantidade = 1)
    {
        var resposta = await CriarCliente().PostAsJsonAsync("/api/pedidos", new
        {
            clienteId = cliente,
            regiao = "Sul",
            prioridade = "Normal",
            itens = new[] { new { sku = "A", quantidade, precoUnitario } },
        });
        resposta.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Executa uma consulta direta no banco do teste (inspeção, não atalho para o contrato).</summary>
    public async Task<T> NoBancoAsync<T>(Func<ContextoDePedidos, Task<T>> consulta)
    {
        await using var escopo = Services.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<ContextoDePedidos>());
    }

    /// <summary>Conexão SignalR pelo TestServer (Long Polling funciona em processo).</summary>
    public HubConnection CriarConexao(string? token)
    {
        _ = Server; // garante o servidor iniciado
        return new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "hubs/pedidos"), opcoes =>
            {
                opcoes.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                opcoes.Transports = HttpTransportType.LongPolling;
                if (token is not null)
                {
                    opcoes.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
    }

    /// <summary>Ordem do descarte: primeiro o host (worker drena, contextos fecham), depois o banco.</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _conexaoGuardia.Dispose(); // última conexão: o banco em memória deixa de existir
        }
    }

    private static string NovaSenha() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    private sealed record RespostaDeToken(string AccessToken);
}
