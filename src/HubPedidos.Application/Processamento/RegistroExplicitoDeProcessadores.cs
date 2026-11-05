using HubPedidos.Application.Processamento.Processadores;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento;

/// <summary>
/// Registro explícito, sem reflexão, usado para comparar as duas abordagens
/// (Roteiro 1, etapa 4). Cada novo processador exige editar esta lista; em troca,
/// o compilador enxerga todas as dependências e nada depende de metadados.
/// </summary>
public static class RegistroExplicitoDeProcessadores
{
    public static CatalogoDeProcessadores Criar() =>
        CatalogoDeProcessadores.CriarComRegistroExplicito(
        [
            (CategoriaProcessamento.Padrao, 1, new ProcessadorPadrao()),
            (CategoriaProcessamento.Prioritaria, 1, new ProcessadorPrioritario()),
            (CategoriaProcessamento.Expressa, 1, new ProcessadorExpresso()),
            (CategoriaProcessamento.Expressa, 2, new ProcessadorExpressoV2()),
            (CategoriaProcessamento.Volumosa, 1, new ProcessadorVolumoso()),
            (CategoriaProcessamento.AltoValor, 1, new ProcessadorAltoValor()),
        ]);
}
