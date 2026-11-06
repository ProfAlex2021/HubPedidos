using HubPedidos.Application.Processamento;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Api.Composicao;

/// <summary>
/// Tarefas de inicialização do banco, executadas pelo composition root antes de
/// aceitar requisições: migrations (quando configurado), modo de journal do SQLite e
/// massa de dados opcional ("--semear N").
/// </summary>
public static class PreparacaoDoBanco
{
    public static async Task ExecutarAsync(WebApplication app, string[] args)
    {
        await using var escopo = app.Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoDePedidos>();

        if (app.Configuration.GetValue<bool>("Banco:AplicarMigracoesNaInicializacao"))
        {
            await contexto.Database.MigrateAsync();
            app.Logger.LogInformation("Migrations aplicadas.");
        }

        // Roteiro 5: modo de journal do SQLite explícito ("DELETE" é o padrão do SQLite;
        // "WAL" foi a intervenção adotada). O modo WAL fica gravado no arquivo do banco.
        if (app.Configuration["Banco:ModoJournal"] is { Length: > 0 } modo)
        {
            if (modo is not ("DELETE" or "WAL"))
            {
                throw new InvalidOperationException("Banco:ModoJournal deve ser DELETE ou WAL.");
            }

            // A conexão pertence ao contexto: abre e fecha por ele, sem descartá-la.
            await contexto.Database.OpenConnectionAsync();
            await using (var comando = contexto.Database.GetDbConnection().CreateCommand())
            {
                comando.CommandText = $"PRAGMA journal_mode={modo};";
                app.Logger.LogInformation("SQLite journal_mode = {Modo}.", await comando.ExecuteScalarAsync());
            }

            await contexto.Database.CloseConnectionAsync();
        }

        var indice = Array.IndexOf(args, "--semear");
        if (indice >= 0 && indice + 1 < args.Length && int.TryParse(args[indice + 1], out var quantidade))
        {
            var total = await SemeadorDePedidos.SemearAsync(
                contexto, app.Services.GetRequiredService<CatalogoDeProcessadores>(), quantidade, DateTime.UtcNow);
            app.Logger.LogInformation("Massa de dados gerada: {Total} pedidos no banco.", total);
        }
    }
}
