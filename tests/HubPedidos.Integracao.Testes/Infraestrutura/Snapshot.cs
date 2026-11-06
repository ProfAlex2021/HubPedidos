using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HubPedidos.Integracao.Testes.Infraestrutura;

/// <summary>
/// Teste de contrato por snapshot (extensão do Roteiro 8) com revisão EXPLÍCITA.
/// </summary>
/// <remarks>
/// O JSON aprovado fica em Contratos/Snapshots/{nome}.json, versionado no Git. Se a
/// resposta mudar, o teste falha e grava ao lado {nome}.recebido.json (ignorado pelo
/// Git). Aceitar a mudança é uma decisão humana: comparar os dois arquivos e, se a
/// mudança for intencional e compatível, substituir o aprovado pelo recebido no mesmo
/// commit que altera o contrato. Nada é atualizado automaticamente.
/// Valores que mudam a cada execução (identificador, instantes) são trocados por
/// marcadores antes da comparação (só no nível raiz, onde estão); nomes, tipos e
/// estrutura continuam comparados.
/// </remarks>
public static class Snapshot
{
    // Sem escapes (ç, <): o arquivo é lido por pessoas na revisão. Só para o snapshot.
    private static readonly JsonSerializerOptions Indentado = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Verificar(string json, string nome, IReadOnlyCollection<string> camposVariaveis)
    {
        // Localiza a pasta pela raiz do repositório (onde está o .slnx), e não pelo caminho
        // de compilação: em CI o compilador pode remapear caminhos de código-fonte.
        var pasta = Path.Combine(Repositorio.Raiz, "tests", "HubPedidos.Integracao.Testes", "Contratos", "Snapshots");
        var aprovado = Path.Combine(pasta, $"{nome}.json");
        var recebido = Path.Combine(pasta, $"{nome}.recebido.json");

        var normalizado = MarcarCamposVariaveis(JsonNode.Parse(json)!.AsObject(), camposVariaveis).ToJsonString(Indentado).ReplaceLineEndings("\n") + "\n";

        if (File.Exists(aprovado) && File.ReadAllText(aprovado).ReplaceLineEndings("\n") == normalizado)
        {
            File.Delete(recebido); // sobra de uma falha anterior já resolvida
            return;
        }

        File.WriteAllText(recebido, normalizado);
        Assert.Fail(
            $"O contrato '{nome}' mudou (ou ainda não foi aprovado). Compare:\n" +
            $"  aprovado: {aprovado}\n  recebido: {recebido}\n" +
            "Se a mudança for intencional e compatível, substitua o aprovado pelo recebido no mesmo commit da mudança.");
    }

    private static JsonObject MarcarCamposVariaveis(JsonObject raiz, IReadOnlyCollection<string> camposVariaveis)
    {
        foreach (var campo in camposVariaveis)
        {
            if (raiz[campo] is { } valor)
            {
                // O marcador guarda o TIPO do valor: trocar uma data por número ainda falha.
                raiz[campo] = JsonValue.Create($"<{campo}:{valor.GetValueKind().ToString().ToLowerInvariant()}>");
            }
        }

        return raiz;
    }
}
