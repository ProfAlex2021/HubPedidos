using System.Diagnostics;
using HubPedidos.Domain.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HubPedidos.Application.Processamento.Fila;

/// <summary>
/// Executa o processamento de UM pedido. Vive em um escopo de DI criado por item,
/// para que dependências scoped (DbContext) nunca sejam compartilhadas entre consumidores.
/// </summary>
public interface IProcessamentoDePedido
{
    Task<ResultadoDoProcessamento> ProcessarAsync(PedidoId pedidoId, CancellationToken cancellationToken);
}

/// <summary>
/// Consumidores permanentes da fila. Política de encerramento: DRENAR.
/// </summary>
/// <remarks>
/// - No encerramento (StopAsync), a fila é concluída: nenhuma admissão nova, e os
///   consumidores continuam até esvaziá-la.
/// - Se o prazo de encerramento do host se esgotar, o token forçado é cancelado e os
///   consumidores param. Os pedidos não processados continuam "Recebido" no banco e
///   podem ser reenviados por POST /api/processamento/pendentes (o processamento é
///   idempotente).
/// - A falha de um item é registrada com o identificador do pedido e não derruba o
///   consumidor; os demais itens continuam.
/// </remarks>
public sealed class ProcessadorDePedidosWorker(
    FilaDeProcessamento fila,
    MetricasDoProcessamento metricas,
    IServiceScopeFactory fabricaDeEscopos,
    ILogger<ProcessadorDePedidosWorker> logger) : BackgroundService
{
    private readonly CancellationTokenSource _cancelamentoForcado = new();

    /// <summary>Tarefa que termina quando todos os consumidores terminaram.</summary>
    public Task Consumidores { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Os consumidores são criados aqui, de forma síncrona, e não em ExecuteAsync: no
    /// .NET 10 o ExecuteAsync roda em segundo plano, e um StopAsync logo após o início
    /// encontraria "Consumidores" ainda vazio e encerraria sem drenar (defeito
    /// encontrado pelo teste de encerramento).
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Os consumidores NÃO observam o stoppingToken: ele é cancelado logo no início do
        // encerramento e impediria a drenagem. Eles observam apenas o token forçado.
        var token = _cancelamentoForcado.Token;
        Consumidores = Task.WhenAll(Enumerable.Range(1, fila.Opcoes.Consumidores)
            .Select(numero => Task.Run(() => ConsumirAsync(numero, token), CancellationToken.None)));
        logger.LogInformation(
            "Worker iniciado: {Consumidores} consumidores, capacidade {Capacidade}, modo {Modo}.",
            fila.Opcoes.Consumidores, fila.Opcoes.Capacidade, fila.Opcoes.ModoQuandoCheia);
        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Consumidores;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        fila.Concluir();
        logger.LogInformation("Encerrando: fila concluída com {Pendentes} itens para drenar.", fila.Profundidade);

        // Quando o prazo do host acabar, força a parada dos consumidores.
        await using var registro = cancellationToken.Register(() => _cancelamentoForcado.Cancel());
        try
        {
            await Consumidores.WaitAsync(cancellationToken);
            logger.LogInformation("Fila drenada; consumidores encerrados normalmente.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Prazo de encerramento esgotado; {Restantes} itens ficaram na fila e continuam 'Recebido' no banco.", fila.Profundidade);
        }

        await base.StopAsync(CancellationToken.None);
    }

    public override void Dispose()
    {
        _cancelamentoForcado.Dispose();
        base.Dispose();
    }

    private async Task ConsumirAsync(int numero, CancellationToken token)
    {
        try
        {
            // ReadAllAsync aguarda itens e termina sozinho quando a fila é concluída e esvaziada.
            await foreach (var job in fila.Leitor.ReadAllAsync(token))
            {
                await ProcessarItemAsync(job, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.LogWarning("Consumidor {Numero} interrompido pelo encerramento forçado.", numero);
        }
    }

    private async Task ProcessarItemAsync(ProcessarPedidoJob job, CancellationToken token)
    {
        metricas.RegistrarEsperaNaFila(DateTime.UtcNow - job.EnfileiradoEmUtc);

        // Mesmo pedido enfileirado duas vezes e lido por dois consumidores: só um processa.
        if (!metricas.TentarIniciar(job.PedidoId))
        {
            metricas.RegistrarResultado(ResultadoDoProcessamento.IgnoradoPorIdempotencia, TimeSpan.Zero);
            return;
        }

        var inicio = Stopwatch.GetTimestamp();
        try
        {
            // Um escopo por item: DbContext e demais serviços scoped não são compartilhados.
            await using var escopo = fabricaDeEscopos.CreateAsyncScope();
            // Único ponto em que o contêiner é consultado: o worker é infraestrutura de
            // composição (cria um escopo por item). O caso de uso recebe tudo por construtor.
            var execucao = escopo.ServiceProvider.GetRequiredService<ExecucaoDoProcessamento>();
            var resultado = await execucao.ExecutarAsync(job.PedidoId, token);

            var duracao = Stopwatch.GetElapsedTime(inicio);
            metricas.RegistrarResultado(resultado, duracao);
            if (resultado == ResultadoDoProcessamento.Processado)
            {
                Diagnostico.TelemetriaDoHub.PedidosProcessados.Add(1);
            }
            logger.LogInformation("Pedido {PedidoId}: {Resultado} em {DuracaoMs:F1} ms.", job.PedidoId, resultado, duracao.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw; // encerramento forçado: propaga para o laço do consumidor
        }
        catch (Exception excecao)
        {
            // Falha visível, com o identificador do pedido, sem derrubar o consumidor.
            metricas.RegistrarFalha();
            logger.LogError(excecao, "Falha ao processar o pedido {PedidoId} após {DuracaoMs:F1} ms.",
                job.PedidoId, Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
        }
        finally
        {
            metricas.Finalizar(job.PedidoId);
        }
    }
}
