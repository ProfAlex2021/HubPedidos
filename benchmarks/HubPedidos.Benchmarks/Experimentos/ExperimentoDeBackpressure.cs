using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using HubPedidos.Application.Processamento;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Infrastructure.Processamento;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HubPedidos.Benchmarks.Experimentos;

/// <summary>
/// Roteiro 4, etapa 4: produz carga ACIMA da capacidade, com taxa controlada, contra a
/// fila e o worker reais e um SQLite real em arquivo, e compara configurações.
/// Não é um BenchmarkDotNet: mede o comportamento do sistema (fila, rejeições,
/// descartes, espera, vazão) durante alguns segundos.
/// Uso: dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --experimento-fila
/// </summary>
public static class ExperimentoDeBackpressure
{
    private const int TaxaPorSegundo = 300;
    private const int DuracaoDaCargaEmSegundos = 5;

    private static readonly (string Nome, OpcoesDaFila Opcoes)[] Configuracoes =
    [
        ("A: fila 50, 2 consumidores, banco 1, Wait", new() { Capacidade = 50, Consumidores = 2, LimiteDeAcessoAoBanco = 1 }),
        ("B: fila 500, 8 consumidores, banco 1, Wait", new() { Capacidade = 500, Consumidores = 8, LimiteDeAcessoAoBanco = 1 }),
        ("C: fila 500, 8 consumidores, banco 8, Wait", new() { Capacidade = 500, Consumidores = 8, LimiteDeAcessoAoBanco = 8 }),
        ("D: fila 50, 2 consumidores, banco 1, DropOldest", new() { Capacidade = 50, Consumidores = 2, LimiteDeAcessoAoBanco = 1, ModoQuandoCheia = BoundedChannelFullMode.DropOldest }),
    ];

    public static async Task ExecutarAsync()
    {
        Console.WriteLine($"Carga: {TaxaPorSegundo} pedidos/s durante {DuracaoDaCargaEmSegundos} s (admissão por TryWrite). " +
                          $"Trabalho externo simulado: Task.Delay(20 ms) por pedido; banco: UPDATE real no SQLite em arquivo.");
        Console.WriteLine($"Ambiente: {Environment.OSVersion}, {Environment.ProcessorCount} núcleos lógicos, .NET {Environment.Version}.");
        Console.WriteLine();
        Console.WriteLine("| Configuração | Aceitos | Rejeitados | Descartados | Processados | Profundidade máx. | Espera média na fila (ms) | Espera máx. (ms) | Duração média do item (ms) | Pico banco | Vazão (pedidos/s) |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var (nome, opcoes) in Configuracoes)
        {
            var r = await ExecutarConfiguracaoAsync(opcoes);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {nome} | {r.Metricas.Aceitos} | {r.Metricas.Rejeitados} | {r.Metricas.Descartados} | {r.Metricas.Processados} | {r.ProfundidadeMaxima} | {r.Metricas.EsperaMediaNaFilaMs:F0} | {r.Metricas.EsperaMaximaNaFilaMs:F0} | {r.Metricas.DuracaoMediaMs:F1} | {r.Metricas.PicoDeAcessoAoBanco} | {r.Vazao:F0} |"));
        }
    }

    private static async Task<(InstantaneoDasMetricas Metricas, int ProfundidadeMaxima, double Vazao)> ExecutarConfiguracaoAsync(OpcoesDaFila opcoes)
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"hubpedidos-exp-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={arquivo};Pooling=False";
        var opcoesDoEf = new DbContextOptionsBuilder<ContextoDePedidos>().UseSqlite(connectionString).Options;

        try
        {
            var ids = await PrepararBancoAsync(opcoesDoEf, TaxaPorSegundo * DuracaoDaCargaEmSegundos);

            using var limitador = new LimitadorDeAcessoAoBanco(opcoes.LimiteDeAcessoAoBanco);
            var servicos = new ServiceCollection()
                .AddSingleton(opcoes)
                .AddSingleton(limitador)
                .AddSingleton(TimeProvider.System)
                .AddScoped(_ => new ContextoDePedidos(opcoesDoEf))
                .AddScoped<IProcessamentoDePedido, ProcessamentoDePedidoEfCore>()
                // Roteiro 6: o worker resolve a execução (processa e notifica).
                .AddSingleton<HubPedidos.Application.Notificacoes.INotificadorDePedidos>(
                    new HubPedidos.Infrastructure.Notificacoes.NotificadorPorLog(NullLogger<HubPedidos.Infrastructure.Notificacoes.NotificadorPorLog>.Instance))
                .AddScoped<ExecucaoDoProcessamento>();
            await using var provedor = servicos.BuildServiceProvider();

            var metricas = new MetricasDoProcessamento();
            var fila = new FilaDeProcessamento(opcoes, metricas);
            using var worker = new ProcessadorDePedidosWorker(fila, metricas, provedor.GetRequiredService<IServiceScopeFactory>(), NullLogger<ProcessadorDePedidosWorker>.Instance);
            await worker.StartAsync(CancellationToken.None);

            // Produtor com taxa controlada pelo relógio (não pelo número de "ticks" do timer).
            var profundidadeMaxima = 0;
            var relogio = Stopwatch.StartNew();
            var emitidos = 0;
            using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10)))
            {
                while (relogio.Elapsed.TotalSeconds < DuracaoDaCargaEmSegundos && await timer.WaitForNextTickAsync())
                {
                    var devido = Math.Min(ids.Count, (int)(relogio.Elapsed.TotalSeconds * TaxaPorSegundo));
                    while (emitidos < devido)
                    {
                        fila.TentarEnfileirar(ids[emitidos++]);
                    }

                    profundidadeMaxima = Math.Max(profundidadeMaxima, fila.Profundidade);
                }
            }

            // Drena o que foi aceito e mede a vazão até o fim.
            await worker.StopAsync(CancellationToken.None);
            var duracao = relogio.Elapsed.TotalSeconds;
            var instantaneo = metricas.Instantaneo(fila.Profundidade, limitador.Pico);
            return (instantaneo, profundidadeMaxima, instantaneo.Processados / duracao);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(arquivo);
        }
    }

    private static async Task<List<PedidoId>> PrepararBancoAsync(DbContextOptions<ContextoDePedidos> opcoesDoEf, int quantidade)
    {
        await using var contexto = new ContextoDePedidos(opcoesDoEf);
        await contexto.Database.MigrateAsync();
        await SemeadorDePedidos.SemearAsync(contexto, CatalogoDeProcessadores.DescobrirPadrao(), quantidade, DateTime.UtcNow);
        return [.. (await contexto.Pedidos.OrderBy(p => p.Numero).Select(p => p.Id).ToListAsync()).Select(id => new PedidoId(id))];
    }
}
