using System.Globalization;
using System.Text;

namespace HubPedidos.Carga;

/// <summary>Linha do relatório: uma rota em uma execução.</summary>
public sealed record LinhaDoRelatorio(
    string Rotulo,
    string Rota,
    int Requisicoes,
    int Sucessos,
    int Erros,
    int Timeouts,
    int NaoEnviadas,
    double VazaoPorSegundo,
    ResumoDeLatencias Latencias);

/// <summary>Consolida amostras por rota. Os percentis incluem erros e timeouts (critério declarado).</summary>
public static class RelatorioDeCarga
{
    public static IReadOnlyList<LinhaDoRelatorio> Consolidar(string rotulo, ResultadoDaJanela janela) =>
    [
        .. janela.Amostras
            .GroupBy(a => a.Rota)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var enviadas = g.Where(a => a.Desfecho != Desfecho.NaoEnviada).ToArray();
                return new LinhaDoRelatorio(
                    rotulo,
                    g.Key,
                    g.Count(),
                    g.Count(a => a.Desfecho == Desfecho.Sucesso),
                    g.Count(a => a.Desfecho == Desfecho.Erro),
                    g.Count(a => a.Desfecho == Desfecho.Timeout),
                    g.Count(a => a.Desfecho == Desfecho.NaoEnviada),
                    g.Count(a => a.Desfecho == Desfecho.Sucesso) / janela.Duracao.TotalSeconds,
                    CalculadoraDePercentis.Calcular(enviadas.Length == 0 ? [0] : enviadas.Select(a => a.LatenciaMs)));
            }),
    ];

    public const string Cabecalho =
        "| Execução | Rota | Req. | Sucesso | Erros | Timeouts | Não enviadas | Vazão (req/s) | Média (ms) | p50 (ms) | p95 (ms) | p99 (ms) | Máx. (ms) |\n" +
        "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|";

    public static string ParaMarkdown(LinhaDoRelatorio l) => string.Create(CultureInfo.InvariantCulture,
        $"| {l.Rotulo} | {l.Rota} | {l.Requisicoes} | {l.Sucessos} | {l.Erros} | {l.Timeouts} | {l.NaoEnviadas} | {l.VazaoPorSegundo:F1} | {l.Latencias.Media:F1} | {l.Latencias.P50:F1} | {l.Latencias.P95:F1} | {l.Latencias.P99:F1} | {l.Latencias.Maximo:F1} |");

    /// <summary>CSV com todas as amostras da janela (para gráficos ou nova análise).</summary>
    public static string ParaCsv(ResultadoDaJanela janela)
    {
        var texto = new StringBuilder("rota,planejado_ms,latencia_ms,status,desfecho\n");
        foreach (var a in janela.Amostras)
        {
            texto.Append(CultureInfo.InvariantCulture, $"{a.Rota},{a.PlanejadoEmMs:F3},{a.LatenciaMs:F3},{a.Status},{a.Desfecho}\n");
        }

        return texto.ToString();
    }
}
