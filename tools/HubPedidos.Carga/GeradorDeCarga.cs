using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HubPedidos.Carga;

/// <summary>Parâmetros de uma execução de carga.</summary>
public sealed record OpcoesDaCarga(
    double TaxaPorSegundo,
    TimeSpan Aquecimento,
    TimeSpan Medicao,
    double ProporcaoDeEscrita = 0.3,
    TimeSpan? Timeout = null,
    int MaximoEmVoo = 512)
{
    public TimeSpan TimeoutEfetivo => Timeout ?? TimeSpan.FromSeconds(2);
}

/// <summary>Como uma requisição terminou.</summary>
public enum Desfecho
{
    Sucesso,
    Erro,
    Timeout,

    /// <summary>O gerador já tinha o máximo de requisições em voo e não enviou esta.</summary>
    NaoEnviada,
}

/// <summary>Uma requisição medida. A latência é contada a partir do instante PLANEJADO.</summary>
public sealed record Amostra(string Rota, double PlanejadoEmMs, double LatenciaMs, int Status, Desfecho Desfecho);

/// <summary>Amostras de uma janela de medição e sua duração real.</summary>
public sealed record ResultadoDaJanela(IReadOnlyList<Amostra> Amostras, TimeSpan Duracao);

/// <summary>
/// Gerador de carga em MODELO ABERTO: as requisições são disparadas em uma taxa fixa,
/// independentemente de as anteriores terem terminado (como usuários reais chegando).
/// </summary>
/// <remarks>
/// - Aquecimento e medição são janelas separadas; só a medição é registrada.
/// - A latência vai do instante planejado até a conclusão. Se o sistema atrasa, a espera
///   entra na medida, evitando a "omissão coordenada" de um gerador que só dispara a
///   próxima requisição depois que a anterior volta.
/// - Erros (status diferente de 2xx, falha de conexão) e timeouts são registrados e
///   entram nos percentis: uma falha não desaparece da amostra.
/// - Mistura determinística: a requisição i é escrita quando (i × 37) mod 100 &lt; proporção × 100.
/// </remarks>
public sealed class GeradorDeCarga(HttpClient cliente, OpcoesDaCarga opcoes)
{
    public const string RotaDeConsulta = "GET /api/pedidos";
    public const string RotaDeCriacao = "POST /api/pedidos";

    public async Task<ResultadoDaJanela> ExecutarAsync(CancellationToken cancellationToken = default)
    {
        if (opcoes.Aquecimento > TimeSpan.Zero)
        {
            await ExecutarJanelaAsync(opcoes.Aquecimento, registrar: false, cancellationToken);
        }

        return await ExecutarJanelaAsync(opcoes.Medicao, registrar: true, cancellationToken);
    }

    private async Task<ResultadoDaJanela> ExecutarJanelaAsync(TimeSpan duracao, bool registrar, CancellationToken cancellationToken)
    {
        var amostras = new ConcurrentBag<Amostra>();
        var tarefas = new List<Task>();
        var emVoo = 0;
        var total = (int)(duracao.TotalSeconds * opcoes.TaxaPorSegundo);
        var relogio = Stopwatch.StartNew();

        for (var i = 0; i < total; i++)
        {
            var planejado = TimeSpan.FromSeconds(i / opcoes.TaxaPorSegundo);
            var espera = planejado - relogio.Elapsed;
            if (espera > TimeSpan.FromMilliseconds(1))
            {
                await Task.Delay(espera, cancellationToken);
            }

            var escrita = i * 37 % 100 < opcoes.ProporcaoDeEscrita * 100;
            if (Volatile.Read(ref emVoo) >= opcoes.MaximoEmVoo)
            {
                if (registrar)
                {
                    amostras.Add(new Amostra(escrita ? RotaDeCriacao : RotaDeConsulta, planejado.TotalMilliseconds, 0, 0, Desfecho.NaoEnviada));
                }

                continue;
            }

            Interlocked.Increment(ref emVoo);
            var numero = i;
            tarefas.Add(Task.Run(async () =>
            {
                try
                {
                    var amostra = await EnviarAsync(numero, escrita, planejado, relogio, cancellationToken);
                    if (registrar)
                    {
                        amostras.Add(amostra);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref emVoo);
                }
            }, CancellationToken.None));
        }

        await Task.WhenAll(tarefas);
        return new ResultadoDaJanela([.. amostras.OrderBy(a => a.PlanejadoEmMs)], relogio.Elapsed);
    }

    private async Task<Amostra> EnviarAsync(int numero, bool escrita, TimeSpan planejado, Stopwatch relogio, CancellationToken cancellationToken)
    {
        var rota = escrita ? RotaDeCriacao : RotaDeConsulta;
        using var requisicao = escrita
            ? new HttpRequestMessage(HttpMethod.Post, "/api/pedidos") { Content = new StringContent(CorpoDeCriacao(numero), Encoding.UTF8, "application/json") }
            : new HttpRequestMessage(HttpMethod.Get, $"/api/pedidos?pagina={1 + (numero % 50)}&tamanho=20");

        // Origem da medida: o instante planejado ou, se a requisição saiu um pouco ANTES
        // dele (o laço só espera atrasos acima de 1 ms), o instante real de envio.
        // Assim a espera causada pelo sistema entra na latência e o valor nunca é negativo.
        var origem = TimeSpan.FromTicks(Math.Min(planejado.Ticks, relogio.Elapsed.Ticks));
        double Latencia() => (relogio.Elapsed - origem).TotalMilliseconds;

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(opcoes.TimeoutEfetivo);
        try
        {
            using var resposta = await cliente.SendAsync(requisicao, limite.Token);
            await resposta.Content.LoadIntoBufferAsync(limite.Token); // a resposta inteira faz parte da latência
            var desfecho = resposta.IsSuccessStatusCode ? Desfecho.Sucesso : Desfecho.Erro;
            return new Amostra(rota, planejado.TotalMilliseconds, Latencia(), (int)resposta.StatusCode, desfecho);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Amostra(rota, planejado.TotalMilliseconds, Latencia(), 0, Desfecho.Timeout);
        }
        catch (HttpRequestException)
        {
            return new Amostra(rota, planejado.TotalMilliseconds, Latencia(), 0, Desfecho.Erro);
        }
    }

    private static string CorpoDeCriacao(int numero) => string.Create(CultureInfo.InvariantCulture,
        $$"""{"clienteId":"carga-{{numero % 200:D3}}","regiao":"Sudeste","prioridade":"Normal","itens":[{"sku":"SKU-{{numero % 300:D3}}","quantidade":{{1 + numero % 5}},"precoUnitario":{{10 + numero % 90}}.90},{"sku":"CABO","quantidade":1,"precoUnitario":5.00}]}""");
}
