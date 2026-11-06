using HubPedidos.Api.Seguranca;
using HubPedidos.Api.TempoReal;
using HubPedidos.Application.Pedidos.Portas;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Api.Endpoints;

/// <summary>Credenciais do login.</summary>
public sealed record CredenciaisRequest(string? Usuario, string? Senha);

/// <summary>Roteiro 7: login, revogação de acesso e leitura pública com output cache.</summary>
public static class TempoRealEndpoints
{
    public const string PoliticaDeCacheDasEstatisticas = "estatisticas";

    public static IEndpointRouteBuilder MapTempoRealEndpoints(this IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/api/autenticacao/token", EmitirToken)
            .WithName("EmitirToken").WithGroupName("v2").AllowAnonymous();

        rotas.MapPost("/api/admin/usuarios/{usuario}/revogacao", RevogarAsync)
            .WithName("RevogarAcesso").WithGroupName("v2")
            .RequireAuthorization(politica => politica.RequireRole(Papeis.Operador));

        // Leitura PÚBLICA, idempotente e agregada (sem dados de cliente): a única com
        // output cache. Rotas autenticadas e personalizadas ficam fora do cache do servidor.
        rotas.MapGet("/api/estatisticas/categorias", ObterEstatisticasAsync)
            .WithName("EstatisticasPorCategoria").WithGroupName("v1")
            .AllowAnonymous()
            .CacheOutput(PoliticaDeCacheDasEstatisticas);

        rotas.MapHub<PedidosHub>(PedidosHub.Rota);
        return rotas;
    }

    /// <summary>
    /// Emite um bearer token (esquema BearerToken do ASP.NET Core). Resposta 200:
    /// <c>{"tokenType":"Bearer","accessToken":"...","expiresIn":3600,"refreshToken":"..."}</c>.
    /// </summary>
    public static IResult EmitirToken(CredenciaisRequest credenciais, ServicoDeAutenticacao autenticacao) =>
        autenticacao.Autenticar(credenciais.Usuario, credenciais.Senha) is { } usuario
            ? TypedResults.SignIn(usuario, authenticationScheme: BearerTokenDefaults.AuthenticationScheme)
            : TypedResults.Unauthorized();

    public static async Task<Ok<int>> RevogarAsync(string usuario, ServicoDeRevogacao revogacao, CancellationToken cancellationToken) =>
        TypedResults.Ok(await revogacao.RevogarAsync(usuario, cancellationToken));

    /// <summary>
    /// O cabeçalho X-Gerado-Em mostra quando a resposta foi produzida: o mesmo valor em
    /// chamadas seguidas indica um acerto de cache.
    /// </summary>
    public static async Task<Ok<IReadOnlyList<HubPedidos.Application.Pedidos.Contratos.EstatisticaPorCategoria>>> ObterEstatisticasAsync(
        IConsultasDePedidos consultas, TimeProvider relogio, HttpResponse resposta, CancellationToken cancellationToken, int dias = 30)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        resposta.Headers["X-Gerado-Em"] = agora.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return TypedResults.Ok(await consultas.ContarPorCategoriaAsync(agora.AddDays(-Math.Clamp(dias, 1, 365)), cancellationToken));
    }
}
