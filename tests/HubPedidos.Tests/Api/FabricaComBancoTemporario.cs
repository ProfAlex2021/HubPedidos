using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace HubPedidos.Tests.Api;

/// <summary>
/// Hospeda a API real em processo (TestServer) com um banco SQLite em arquivo
/// temporário, criado pelas migrations na inicialização e apagado no Dispose.
/// </summary>
/// <remarks>
/// Ambiente "Testes": não carrega User Secrets, então o banco de desenvolvimento nunca é
/// usado por acidente. (O Roteiro 8 troca esta fábrica simples por uma fábrica
/// customizada com banco em memória e substituição de serviços.)
/// </remarks>
internal sealed class FabricaComBancoTemporario : WebApplicationFactory<Program>
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"hubpedidos-teste-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:HubPedidos", $"Data Source={_arquivo}");
        builder.UseSetting("Banco:AplicarMigracoesNaInicializacao", "true");
        builder.UseSetting("Fila:DuracaoSimuladaEmMs", "0");
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
}
