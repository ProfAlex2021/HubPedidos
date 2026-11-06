# ADR 0001 — Módulos e direção das dependências

- **Data:** 2026-11-06 (Roteiro 6)
- **Situação:** aceita

## Contexto

Até o Roteiro 5 os endpoints usavam diretamente `ContextoDePedidos`, `RepositorioDePedidos`
e `ConsultasDePedidos` da Infrastructure, e o `Program.cs` reunia todos os registros. Uma
mudança de persistência exigia mexer em endpoints, e nada impedia um endpoint de montar
consultas EF por conta própria.

## Decisão

- Três módulos com responsabilidades claras: **Pedidos** (recepção, classificação,
  criação, cancelamento, consultas, lotes), **Processamento** (fila, worker, limite de
  acesso ao banco) e **Notificações** (publicação de mudanças de situação).
- As interfaces (portas) ficam na **fronteira que as consome**, a Application
  (`IRepositorioDePedidos`, `IConsultasDePedidos`, `INotificadorDePedidos`,
  `IProcessamentoDePedido`); a Infrastructure as implementa.
- Direção permitida: `Api → Application → Domain` e `Infrastructure → Application → Domain`.
  A Api conhece a Infrastructure **somente** no composition root.
- Mantidos 4 projetos (Domain, Application, Infrastructure, Api). Os módulos são namespaces
  com extensões de registro próprias, e não assemblies separados.

## Alternativas descartadas

- **Um projeto por módulo** (Pedidos.Application, Processamento.Application...): mais
  isolamento de compilação, mas o domínio é pequeno e os módulos compartilham a entidade
  `Pedido`; seriam 8+ projetos sem ganho de distribuição ou de equipe.
- **Repositório genérico `IRepository<T>`**: esconderia as consultas específicas (painel,
  cursor, relatório) atrás de `IQueryable`, vazando o EF para a aplicação.
- **Endpoints usando o DbContext diretamente** (como até o Roteiro 5): simples, mas acopla
  HTTP e persistência.

## Consequências

- Verificável por testes (`Arquitetura/DependenciasTestes`): Domain sem referências,
  Application sem Api/EF/ASP.NET, Infrastructure sem Api, endpoints sem tipos de
  infraestrutura e cada porta implementada só na infraestrutura.
- Trocar a notificação por SignalR (Roteiro 7) é um novo registro, sem mudar quem publica.
