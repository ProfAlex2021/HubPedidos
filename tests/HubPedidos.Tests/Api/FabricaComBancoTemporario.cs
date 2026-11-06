using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;

namespace HubPedidos.Tests.Api;

/// <summary>
/// Hospeda a API real em processo (TestServer) com um banco SQLite em arquivo
/// temporário, criado pelas migrations na inicialização e apagado no Dispose.
/// </summary>
/// <remarks>
/// Ambiente "Testes": não carrega User Secrets, então o banco e os usuários de
/// desenvolvimento nunca são usados por acidente. Os usuários de teste recebem senhas
/// aleatórias geradas a cada execução, que nunca são gravadas em arquivo.
/// (O Roteiro 8 troca esta fábrica simples por uma fábrica customizada com banco em
/// memória e substituição de serviços.)
/// </remarks>
internal sealed class FabricaComBancoTemporario : WebApplicationFactory<Program>
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"hubpedidos-teste-{Guid.NewGuid():N}.db");

    /// <summary>Usuários: ana (cliente-001), bruno (cliente-002) e operador.</summary>
    public IReadOnlyDictionary<string, string> Senhas { get; } = new Dictionary<string, string>
    {
        ["ana"] = NovaSenha(),
        ["bruno"] = NovaSenha(),
        ["operador"] = NovaSenha(),
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:HubPedidos", $"Data Source={_arquivo}");
        builder.UseSetting("Banco:AplicarMigracoesNaInicializacao", "true");
        builder.UseSetting("Fila:DuracaoSimuladaEmMs", "0");

        (string Nome, string Papel, string? Cliente)[] usuarios = [("ana", "cliente", "cliente-001"), ("bruno", "cliente", "cliente-002"), ("operador", "operador", null)];
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
    }

    public async Task<string> ObterTokenAsync(string usuario)
    {
        var resposta = await CreateClient().PostAsJsonAsync("/api/autenticacao/token", new { usuario, senha = Senhas[usuario] });
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<RespostaDeToken>())!.AccessToken;
    }

    public async Task<HttpClient> CriarClienteAutenticadoAsync(string usuario)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ObterTokenAsync(usuario));
        return cliente;
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools(); // libera o arquivo antes de apagá-lo
        foreach (var arquivo in new[] { _arquivo, _arquivo + "-wal", _arquivo + "-shm" })
        {
            File.Delete(arquivo);
        }
    }

    private static string NovaSenha() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    private sealed record RespostaDeToken(string AccessToken);
}
