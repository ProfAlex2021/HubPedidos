// Tipos usados SOMENTE pelos testes do catálogo. Cada namespace é um cenário:
// os testes apontam a descoberta para um namespace específico deste assembly,
// o que também prova que a reflexão respeita o escopo recebido.
#pragma warning disable CA1812 // Tipos instanciados por reflexão.

using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.Valido
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
    public sealed class ProcessadorTestePadrao : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("TestePadrao", 1, 1, "v1");
    }

    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 3)]
    public sealed class ProcessadorTestePadraoV3 : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("TestePadrao", 3, 1, "v3");
    }

    /// <summary>Implementa o contrato, mas não está marcado: fica fora e aparece no diagnóstico.</summary>
    public sealed class ProcessadorSemAtributo : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("SemAtributo", 1, 1, "-");
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.Valido.Interno
{
    /// <summary>Subnamespace do escopo autorizado: deve ser incluído.</summary>
    [ProcessadorPedido(CategoriaProcessamento.Volumosa, versao: 1)]
    public sealed class ProcessadorTesteVolumoso : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("TesteVolumoso", 1, 1, "-");
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.ValidoExtra
{
    /// <summary>
    /// "ValidoExtra" começa com o texto "Valido", mas é OUTRO namespace.
    /// Uma comparação ingênua com StartsWith incluiria este tipo por engano.
    /// </summary>
    [ProcessadorPedido(CategoriaProcessamento.Prioritaria, versao: 1)]
    public sealed class ProcessadorForaDoEscopo : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("Fora", 1, 1, "-");
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.Duplicado
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
    public sealed class SegundoDuplicado : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("B", 1, 1, "-");
    }

    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
    public sealed class PrimeiroDuplicado : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("A", 1, 1, "-");
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.Incompativel
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
    public sealed class MarcadoSemContrato
    {
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.Abstrato
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
    public abstract class ProcessadorAbstratoMarcado : IProcessadorDePedido
    {
        public abstract ResultadoProcessamento Processar(Pedido pedido);
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.SemConstrutor
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
    public sealed class ProcessadorComDependencia(string configuracao) : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new(configuracao, 1, 1, "-");
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.CategoriaInvalida
{
    [ProcessadorPedido(CategoriaProcessamento.Invalida, versao: 1)]
    public sealed class ProcessadorDeInvalidos : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("X", 1, 1, "-");
    }
}

namespace HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.VersaoInvalida
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 0)]
    public sealed class ProcessadorVersaoZero : IProcessadorDePedido
    {
        public ResultadoProcessamento Processar(Pedido pedido) => new("X", 0, 1, "-");
    }
}
