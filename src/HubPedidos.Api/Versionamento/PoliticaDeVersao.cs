using System.Diagnostics.Metrics;
using System.Globalization;
using HubPedidos.Application.Diagnostico;

namespace HubPedidos.Api.Versionamento;

/// <summary>
/// Política de versão da consulta de pedido (Roteiro 6).
/// </summary>
/// <remarks>
/// - A versão é lida do SEGMENTO DA URL (<c>/api/v1/...</c>, <c>/api/v2/...</c>): é visível
///   em logs, proxies e no navegador, e cada versão pode ser testada isoladamente.
/// - Versão AUSENTE (<c>/api/pedidos/{id}</c>, a rota usada antes do versionamento) é
///   tratada como v1, a versão padrão: os clientes antigos continuam funcionando.
/// - Versão NÃO SUPORTADA (<c>/api/v3/...</c>) recebe 400 com ProblemDetails e o
///   cabeçalho <c>api-supported-versions</c>.
/// - A v1 está depreciada: respostas incluem <c>Deprecation</c> (RFC 9745),
///   <c>Sunset</c> (RFC 8594) e <c>Link rel="successor-version"</c>.
/// </remarks>
public static class PoliticaDeVersao
{
    public const string CabecalhoDeVersoesSuportadas = "api-supported-versions";
    public const int VersaoPadrao = 1;
    public static readonly int[] VersoesSuportadas = [1, 2];

    /// <summary>Data em que a depreciação da v1 foi anunciada (início da janela de convivência).</summary>
    public static readonly DateTimeOffset DepreciacaoDaV1 = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Data demonstrativa de retirada da v1 (a revisar antes de virar compromisso real).</summary>
    public static readonly DateTimeOffset RetiradaDaV1 = new(2027, 12, 31, 23, 59, 59, TimeSpan.Zero);

    /// <summary>Telemetria de uso por versão: critério objetivo para decidir a retirada.</summary>
    private static readonly Counter<long> Chamadas =
        TelemetriaDoHub.Medidor.CreateCounter<long>("hubpedidos.api.consulta_pedido.chamadas", "{chamada}", "Consultas de pedido por versão da API");

    public static string VersoesSuportadasComoTexto => string.Join(", ", VersoesSuportadas);

    public static void RegistrarUso(int versao, bool versaoImplicita) =>
        Chamadas.Add(1, new("versao", versao), new("implicita", versaoImplicita));

    /// <summary>Cabeçalhos de ciclo de vida da v1.</summary>
    public static void AplicarCabecalhosDaV1(HttpResponse resposta, string idDoPedido)
    {
        // RFC 9745: data estruturada no formato "@<segundos desde 1970>".
        resposta.Headers["Deprecation"] = "@" + DepreciacaoDaV1.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        resposta.Headers["Sunset"] = RetiradaDaV1.ToString("R", CultureInfo.InvariantCulture);
        resposta.Headers.Link = $"</api/v2/pedidos/{idDoPedido}>; rel=\"successor-version\"";
        resposta.Headers[CabecalhoDeVersoesSuportadas] = VersoesSuportadasComoTexto;
    }
}
