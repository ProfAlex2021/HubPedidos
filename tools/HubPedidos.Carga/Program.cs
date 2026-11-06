using System.Globalization;
using HubPedidos.Carga;

// Script de carga do Roteiro 5. Exemplos:
//   dotnet run -c Release --project tools/HubPedidos.Carga -- --url http://localhost:5080 --taxa 150 --aquecimento 10 --duracao 30 --rotulo antes
//   dotnet run -c Release --project tools/HubPedidos.Carga -- --degraus 50,100,200,400 --aquecimento 5 --duracao 15   (extensão: saturação)
var argumentos = LerArgumentos(args);
var url = new Uri(argumentos.GetValueOrDefault("url", "http://localhost:5080"));
var aquecimento = TimeSpan.FromSeconds(Numero("aquecimento", 10));
var medicao = TimeSpan.FromSeconds(Numero("duracao", 30));
var proporcaoDeEscrita = Numero("escrita", 0.3);
var timeout = TimeSpan.FromMilliseconds(Numero("timeout-ms", 2000));
var rotulo = argumentos.GetValueOrDefault("rotulo", "execucao");
var saida = argumentos.GetValueOrDefault("saida");
double[] taxas = argumentos.TryGetValue("degraus", out var degraus)
    ? [.. degraus.Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture))]
    : [Numero("taxa", 150)];

using var cliente = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 512, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
{
    BaseAddress = url,
    Timeout = Timeout.InfiniteTimeSpan, // o timeout é controlado por requisição no gerador
};

Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"Alvo {url} | taxas {string.Join('/', taxas)} req/s | aquecimento {aquecimento.TotalSeconds} s | medição {medicao.TotalSeconds} s | escrita {proporcaoDeEscrita:P0} | timeout {timeout.TotalMilliseconds} ms | início {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
Console.WriteLine(RelatorioDeCarga.Cabecalho);

foreach (var taxa in taxas)
{
    var opcoes = new OpcoesDaCarga(taxa, aquecimento, medicao, proporcaoDeEscrita, timeout);
    var janela = await new GeradorDeCarga(cliente, opcoes).ExecutarAsync();
    var rotuloDaExecucao = taxas.Length > 1 ? $"{rotulo} @{taxa.ToString(CultureInfo.InvariantCulture)}/s" : rotulo;
    foreach (var linha in RelatorioDeCarga.Consolidar(rotuloDaExecucao, janela))
    {
        Console.WriteLine(RelatorioDeCarga.ParaMarkdown(linha));
    }

    if (saida is not null)
    {
        Directory.CreateDirectory(saida);
        await File.WriteAllTextAsync(Path.Combine(saida, $"{rotulo}-{taxa.ToString(CultureInfo.InvariantCulture)}.csv"), RelatorioDeCarga.ParaCsv(janela));
    }
}

double Numero(string nome, double padrao) =>
    argumentos.TryGetValue(nome, out var texto) ? double.Parse(texto, CultureInfo.InvariantCulture) : padrao;

static Dictionary<string, string> LerArgumentos(string[] args)
{
    var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        resultado[args[i].TrimStart('-')] = args[i + 1];
    }

    return resultado;
}
