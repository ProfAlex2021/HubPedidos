using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace HubPedidos.Api.Cache;

/// <summary>
/// ETag e pré-condições HTTP (Roteiro 7).
/// </summary>
/// <remarks>
/// A ETag é FORTE e calculada sobre os bytes JSON da representação v2 (SHA-256). Muda
/// quando qualquer campo público muda (situação, itens, endereço...) e é estável
/// enquanto a representação é a mesma. Por depender da representação, uma ETag da v2
/// nunca vale para outra versão. Não usa horário, Guid aleatório nem GetHashCode.
/// </remarks>
public static class ValidacaoCondicional
{
    /// <summary>"private": só o navegador do usuário guarda; "no-cache": revalida sempre.</summary>
    public const string PoliticaDeCachePrivada = "private, no-cache";

    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    public static (byte[] Corpo, EntityTagHeaderValue ETag) Serializar<T>(T representacao)
    {
        var corpo = JsonSerializer.SerializeToUtf8Bytes(representacao, OpcoesJson);
        var resumo = SHA256.HashData(corpo);
        return (corpo, new EntityTagHeaderValue($"\"sha256-{Convert.ToHexStringLower(resumo)}\"", isWeak: false));
    }

    /// <summary>
    /// If-None-Match usa comparação FRACA (RFC 9110): W/"x" corresponde a "x". Aceita
    /// listas e o curinga *.
    /// </summary>
    public static bool IfNoneMatchCorresponde(HttpRequest requisicao, EntityTagHeaderValue atual)
    {
        var recebidas = requisicao.GetTypedHeaders().IfNoneMatch;
        return recebidas is { Count: > 0 } && recebidas.Any(e => e.Equals(EntityTagHeaderValue.Any) || e.Compare(atual, useStrongComparison: false));
    }

    /// <summary>
    /// If-Match usa comparação FORTE: só a representação exata autoriza a alteração.
    /// Devolve null quando o cabeçalho não foi enviado.
    /// </summary>
    public static bool? IfMatchCorresponde(HttpRequest requisicao, EntityTagHeaderValue atual)
    {
        var recebidas = requisicao.GetTypedHeaders().IfMatch;
        if (recebidas is not { Count: > 0 })
        {
            return null;
        }

        return recebidas.Any(e => e.Equals(EntityTagHeaderValue.Any) || e.Compare(atual, useStrongComparison: true));
    }

    public static void AplicarCabecalhos(HttpResponse resposta, EntityTagHeaderValue etag)
    {
        var cabecalhos = resposta.GetTypedHeaders();
        cabecalhos.ETag = etag;
        resposta.Headers.CacheControl = PoliticaDeCachePrivada;
        resposta.Headers.Vary = "Authorization";
    }
}
