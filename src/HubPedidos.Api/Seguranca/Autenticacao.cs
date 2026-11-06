using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Domain.Pedidos;
using Microsoft.AspNetCore.Authentication.BearerToken;

namespace HubPedidos.Api.Seguranca;

/// <summary>Papéis da aplicação.</summary>
public static class Papeis
{
    public const string Cliente = "cliente";
    public const string Operador = "operador";
}

/// <summary>Claim com o cliente dono dos pedidos que o usuário pode ver.</summary>
public static class Reivindicacoes
{
    public const string ClienteId = "cliente_id";
}

/// <summary>
/// Usuário de teste, lido da seção "Autenticacao:Usuarios". As senhas NÃO ficam no
/// repositório: o script scripts/criar-usuarios-teste.ps1 gera senhas aleatórias e as
/// grava em User Secrets. Nos testes automatizados, a fábrica gera outras a cada execução.
/// </summary>
public sealed class UsuarioDeTeste
{
    public string Nome { get; init; } = string.Empty;

    public string Senha { get; init; } = string.Empty;

    public string Papel { get; init; } = Papeis.Cliente;

    /// <summary>Para o papel "cliente": o clienteId dos pedidos que ele pode acompanhar.</summary>
    public string? ClienteId { get; init; }
}

/// <summary>
/// Usuários revogados (em memória). A revogação vale imediatamente para login, consultas
/// autorizadas e o hub, mesmo que o token emitido antes ainda não tenha expirado.
/// Limitação didática: o registro se perde ao reiniciar o processo.
/// </summary>
public sealed class RegistroDeRevogacoes
{
    private readonly ConcurrentDictionary<string, DateTime> _revogados = new(StringComparer.Ordinal);

    public bool Revogar(string usuario) => _revogados.TryAdd(usuario, DateTime.UtcNow);

    public bool EstaRevogado(string? usuario) => usuario is not null && _revogados.ContainsKey(usuario);
}

/// <summary>Emite o token a partir de usuário e senha.</summary>
public sealed class ServicoDeAutenticacao(IConfiguration configuracao, RegistroDeRevogacoes revogacoes)
{
    public ClaimsPrincipal? Autenticar(string? usuario, string? senha)
    {
        if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(senha) || revogacoes.EstaRevogado(usuario))
        {
            return null;
        }

        var cadastrado = configuracao.GetSection("Autenticacao:Usuarios").Get<UsuarioDeTeste[]>()?
            .FirstOrDefault(u => u.Nome == usuario);
        if (cadastrado is null || string.IsNullOrEmpty(cadastrado.Senha) || !SenhasIguais(cadastrado.Senha, senha))
        {
            return null;
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, cadastrado.Nome), // identifica o usuário no SignalR
            new(ClaimTypes.Name, cadastrado.Nome),
            new(ClaimTypes.Role, cadastrado.Papel),
        ];
        if (cadastrado.ClienteId is { Length: > 0 } clienteId)
        {
            claims.Add(new Claim(Reivindicacoes.ClienteId, clienteId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, BearerTokenDefaults.AuthenticationScheme));
    }

    /// <summary>Comparação em tempo constante: não revela, pelo tempo, quantos caracteres coincidem.</summary>
    private static bool SenhasIguais(string esperada, string informada) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(esperada)),
            SHA256.HashData(Encoding.UTF8.GetBytes(informada)));
}

/// <summary>
/// Regra de acesso a um pedido: operador vê todos; cliente vê só os seus; usuário
/// revogado não vê nenhum. Usada pela consulta v2 e pelo hub, ANTES de qualquer resposta
/// ou associação a grupo.
/// </summary>
public sealed class AutorizacaoDePedidos(IConsultasDePedidos consultas, RegistroDeRevogacoes revogacoes)
{
    /// <summary>Devolve o pedido se o usuário puder acessá-lo; caso contrário, null.</summary>
    public async Task<PedidoResponse?> ObterSePermitidoAsync(ClaimsPrincipal usuario, PedidoId id, CancellationToken cancellationToken)
    {
        if (usuario.Identity?.IsAuthenticated != true || revogacoes.EstaRevogado(usuario.Identity.Name))
        {
            return null;
        }

        var pedido = await consultas.ObterDetalheAsync(id, cancellationToken);
        if (pedido is null)
        {
            return null;
        }

        return usuario.IsInRole(Papeis.Operador) || pedido.ClienteId == usuario.FindFirstValue(Reivindicacoes.ClienteId)
            ? pedido
            : null;
    }
}
