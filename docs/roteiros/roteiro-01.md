# Roteiro 1 — Pattern matching, atributos, reflexão controlada, records e DTOs imutáveis

**Objetivo:** modelar os contratos e o domínio do Hub de Pedidos com tipos imutáveis,
classificação por padrões e descoberta controlada de processadores.

## Como executar

```bash
dotnet build HubPedidos.slnx
dotnet test
dotnet run --project src/HubPedidos.Api --launch-profile http
```

Com a API no ar (`http://localhost:5080`), abra `src/HubPedidos.Api/HubPedidos.Api.http`
no Visual Studio 2026 e envie as requisições da seção "Roteiro 1".

No Visual Studio: abra `HubPedidos.slnx`, defina `HubPedidos.Api` como projeto de
inicialização e use **Teste > Executar Todos os Testes** para os testes.

## Arquivos produzidos

| Camada | Arquivo | Conteúdo |
|---|---|---|
| Domain | `Pedidos/PedidoId.cs` | Value object `readonly record struct` com `IParsable<PedidoId>` |
| Domain | `Pedidos/ItemPedido.cs` | Value object `sealed record` validado no construtor |
| Domain | `Pedidos/Pedido.cs` | Entidade (classe) com cópia defensiva dos itens |
| Domain | `Pedidos/Enumeracoes.cs` | `Regiao`, `Prioridade`, `CategoriaProcessamento` |
| Domain | `Pedidos/ClassificadorDePedidos.cs` | Expressão `switch` com padrões de propriedade, relacionais e lógicos |
| Application | `Pedidos/Contratos/*.cs` | `CriarPedidoRequest`, `ItemPedidoRequest`, `PedidoResponse`, `ItemPedidoResponse`, `ErroDeValidacao` |
| Application | `Pedidos/MapeadorDePedido.cs` | Mapeamento explícito DTO → domínio → DTO, com `ResultadoDeMapeamento<T>` |
| Application | `Pedidos/ServicoDeRecepcaoDePedidos.cs` | Caso de uso: converte, classifica, seleciona e processa |
| Application | `Processamento/ProcessadorPedidoAttribute.cs` | Atributo (categoria + versão) |
| Application | `Processamento/IProcessadorDePedido.cs` | Contrato comum das estratégias |
| Application | `Processamento/Processadores/Processadores.cs` | 6 estratégias (Expressa em v1 e v2) |
| Application | `Processamento/CatalogoDeProcessadores.cs` | Descoberta por reflexão restrita + validações |
| Application | `Processamento/RegistroExplicitoDeProcessadores.cs` | Alternativa sem reflexão, para comparação |
| Api | `Program.cs`, `Endpoints/*.cs` | `POST /api/pedidos`, `GET /api/processadores` |
| Api | `HubPedidos.Api.http` | Requisições de exemplo |
| Tests | `Dominio/`, `Aplicacao/`, `Arquitetura/` | 77 testes MSTest |

## Decisões técnicas

1. **Nomes dos projetos.** O roteiro pede a solução `HubPedidos` com projetos Api, Domain,
   Application e Tests; o livro usa `Pedidos.Validacao`. Segui o roteiro na estrutura
   (`HubPedidos.*`) e o livro no estilo: nomes em português (`MapeadorDePedido`,
   `ClassificadorDePedidos`), MSTest com Microsoft.Testing.Platform, `LangVersion 14.0`
   e `.editorconfig`. Os tipos citados pelo roteiro (`PedidoId`, `ItemPedido`,
   `CriarPedidoRequest`, `PedidoResponse`) mantêm exatamente esses nomes.
2. **Configuração centralizada.** `Directory.Build.props` fixa `net10.0`, C# 14 e
   nulabilidade para todos os projetos; `Directory.Packages.props` (Central Package
   Management) guarda todas as versões em um só lugar; `global.json` fixa o SDK 10.0.401
   e seleciona o MTP para `dotnet test`. O livro repete essas linhas em cada `.csproj`;
   centralizar evita que um projeto novo esqueça alguma delas.
3. **Entidade × value object × DTO.** `Pedido` é classe: sua identidade é o `Id`, e dois
   pedidos com os mesmos dados continuam diferentes. `ItemPedido` e `PedidoId` são records
   (igualdade por valor). Os DTOs são records separados do domínio, com conversão
   explícita no `MapeadorDePedido`.
4. **`PedidoId` como `readonly record struct`.** Ele é pequeno e copiável e impede trocar
   um Guid de pedido por um Guid qualquer. Como toda struct pode ser criada com `default`,
   a propriedade `EhValido` torna esse caso verificável, e o construtor do `Pedido` o rejeita.
5. **Fronteira de validação.** O `MapeadorDePedido` valida todo o DTO e só constrói
   objetos de domínio quando não há erro. Os construtores do domínio validam de novo,
   como última linha de defesa. Região e prioridade chegam como texto: um valor
   desconhecido vira erro legível (400) em vez de exceção de desserialização.
   `Enum.TryParse` aceita números (`"5"`) e combinações (`"Sul,Norte"`), por isso o
   mapeador aceita apenas nomes definidos.
6. **Imutabilidade e coleções.** O `Pedido` copia a lista recebida para um array e o
   expõe por `ReadOnlyCollection`; `PedidoResponse.Itens` é um snapshot novo. `ItemPedido`
   não tem `init`, então `with` não consegue contornar a validação. `PedidoResponse` usa
   `init`, e o teste mostra que `with` faz uma cópia **superficial**, compartilhando a
   coleção não substituída.
7. **Ordem dos braços do `switch`.** Primeiro os dados inválidos, depois AltoValor
   (precedência: urgente acima de R$ 10.000 ainda exige revisão), Expressa, Volumosa,
   Prioritária e Padrão. Não existe braço `_`. O compilador comprova a exaustividade
   (`CS8509` configurado como erro em `HubPedidos.Domain.csproj`) e rejeita braços
   inalcançáveis (`CS8510`). Região ou prioridade fora da enumeração caem no braço
   explícito "desconhecida", nunca em Padrão por acidente.
8. **Reflexão controlada.** O catálogo examina **um assembly recebido** e **um namespace**
   (com subnamespaces, mas sem o falso positivo de prefixo `...ProcessadoresExtras`).
   Processa os tipos em ordem de `FullName`, valida tudo antes de instanciar e falha com
   mensagem legível em caso de duplicidade (categoria + versão), metadado inconsistente
   (marcado sem implementar a interface, abstrato, sem construtor, categoria `Invalida`,
   versão < 1) ou categoria sem processador. É construído uma vez (singleton) e resolvido
   na inicialização da API, que não sobe com configuração inválida. As consultas usam um
   `FrozenDictionary`: depois de pronto, não há nova reflexão.
9. **Registro explícito × reflexão** (etapa 4.3):

   | Critério | Registro explícito | Descoberta por reflexão |
   |---|---|---|
   | Novo processador | Exige editar a lista central | Basta criar a classe com o atributo |
   | Erro de esquecimento | Possível (classe criada e não registrada) | Classe sem atributo aparece no diagnóstico |
   | Verificação | Compilador enxerga tudo | Validações em tempo de execução (testadas) |
   | Custo | Nenhum na inicialização | `GetTypes()` uma vez na inicialização (não medido; não é hot path) |
   | Rastreabilidade | Lista única e legível | Depende de conhecer a convenção do atributo |

   Para este cenário (6 processadores estáveis, mudanças raras), o registro explícito
   seria suficiente. A reflexão foi mantida porque o roteiro pede descoberta e porque o
   versionamento por atributo (Expressa v1 → v2) dispensa editar a lista. O teste
   `Registro_explicito_produz_o_mesmo_catalogo_que_a_reflexao` garante que as duas
   abordagens coincidem.
10. **Onde imutabilidade e reflexão ajudaram ou complicaram** (etapa 4.4). Ajudaram: os
    totais do pedido são calculados uma vez e nunca divergem; a resposta não pode ser
    alterada por quem a recebe; o catálogo inválido falha cedo. Complicaram: a igualdade
    de records não é profunda (respostas com itens equivalentes em arrays diferentes não
    são iguais — teste `Igualdade_de_record_nao_e_profunda_para_colecoes`); a reflexão
    exigiu cenários de teste em namespaces separados e mensagens determinísticas para que
    os erros fossem reproduzíveis.

### Casos cobertos pela classificação

| Classe de equivalência | Exemplo (valor, unidades, região, prioridade) | Categoria |
|---|---|---|
| Pedido comum | 100, 2, Nordeste, Normal | Padrão |
| Limite de valor (inclusivo) | 10.000,00, 1, Sul, Normal | Padrão |
| Limite de volume (inclusivo) | 100, 50, Sul, Normal | Padrão |
| Prioridade alta | 100, 2, Norte, Alta | Prioritária |
| Urgente fora do Sul/Sudeste | 100, 2, Nordeste, Urgente | Prioritária |
| Urgente acima de R$ 5.000 | 5.000,01, 2, Sudeste, Urgente | Prioritária |
| Urgente com 21 unidades | 100, 21, Sudeste, Urgente | Prioritária |
| Expressa | 100, 2, Sul, Urgente | Expressa |
| Limites da expressa | 5.000,00, 20, Sudeste, Urgente | Expressa |
| Alto valor (limite) | 10.000,01, 1, Sul, Normal | AltoValor |
| Alto valor vence urgência | 20.000, 2, Sudeste, Urgente | AltoValor |
| Alto valor vence volume | 20.000, 80, Norte, Alta | AltoValor |
| Volume (limite) | 100, 51, Norte, Normal | Volumosa |
| Volume vence prioridade | 100, 60, Norte, Alta | Volumosa |
| Valor zero / negativo | 0 ou −1 | Inválida |
| Nenhuma unidade | 10, 0 | Inválida |
| Região/prioridade desconhecida | `(Regiao)99`, `(Regiao)0`, `(Prioridade)42` | Inválida |

## Extensões ("Aprimoramentos")

- **`IParsable<PedidoId>`** (conversão com `TryParse` para um value object): rejeita
  texto vazio, inválido e `Guid.Empty`. As Minimal APIs reconhecem o `TryParse`
  estático e farão a conversão de rotas como `/api/pedidos/{id}` sem código adicional.
- **Teste arquitetural** sem pacotes externos (`Arquitetura/DependenciasTestes.cs`): os
  DTOs só expõem tipos da BCL ou outros contratos (nenhum tipo do domínio ou da
  infraestrutura); o Domain não referencia nenhum assembly fora de `System.*`; a
  Application não referencia API, ASP.NET Core nem EF Core.

## Testes obrigatórios do relatório

| Teste obrigatório (Relatório, Atividade 1) | Onde está |
|---|---|
| Classificação de pedido normal, prioritário, inválido e caso-limite | `Dominio/ClassificadorDePedidosTestes.Classifica_cada_classe_de_equivalencia` (20 linhas `DataRow`) e `ServicoDeRecepcaoDePedidosTestes` |
| Pattern matching sem braço inalcançável e com comportamento explícito para entrada desconhecida | Compilador (`CS8509` como erro em `HubPedidos.Domain.csproj`; `CS8510` é erro nativo) + `Todo_braco_do_switch_e_alcancavel_por_algum_caso_de_negocio` + `Entrada_desconhecida_recebe_resultado_explicito_e_nao_cai_em_padrao` |
| Record copiado com `with` sem alterar o original | `Aplicacao/ContratosTestes.Record_copiado_com_with_nao_altera_o_original` e `ClassificadorDePedidosTestes.Perfil_copiado_com_with_nao_altera_o_original` |
| DTO inválido rejeitado antes de criar o objeto de domínio | `Aplicacao/MapeadorDePedidoTestes.DTO_invalido_e_rejeitado_antes_de_criar_o_objeto_de_dominio`; manual: última requisição do `.http` (400 com 6 erros) |
| Reflexão limitada ao assembly/namespace previsto e sem duplicar processadores | `Aplicacao/Catalogo/CatalogoDeProcessadoresTestes` (`Reflexao_de_producao_fica_restrita...`, `Escopo_inclui_subnamespace...`, `Descoberta_e_deterministica_e_nao_duplica...`, `Chave_duplicada_falha...`) |
| Testes de igualdade, cópia, validação e descoberta (evidências) | `PedidoIdTestes`, `PedidoTestes`, `ContratosTestes`, `MapeadorDePedidoTestes`, `CatalogoDeProcessadoresTestes` |

## Evidências de execução

- `dotnet build`: 0 avisos, 0 erros. `dotnet test`: **77 aprovados**, 0 falhas.
- API executada com `dotnet run` e chamada com as requisições do `.http`:
  - `GET /api/processadores` → 6 registros, origem `Reflexao`, 6 tipos no escopo de 37
    tipos do assembly, `ProcessadorExpressoV2` ativo e `ProcessadorExpresso` (v1) inativo.
  - Pedido normal → `Padrao`, `ProcessadorPadrao v1`; urgente no Sudeste → `Expressa`,
    `ProcessadorExpressoV2 v2`; R$ 10.000,00 → `Padrao`; R$ 10.000,01 → `AltoValor`.
  - DTO inválido → `400` com os 6 erros (`clienteId`, `regiao`, `prioridade`,
    `itens[0].sku`, `itens[0].quantidade`, `itens[0].precoUnitario`).

## Versões utilizadas

| Item | Versão |
|---|---|
| Visual Studio | Community 2026 18.10.3 (carga ASP.NET e desenvolvimento Web) |
| SDK .NET | 10.0.401 (runtime 10.0.12), C# 14 |
| MSTest | 4.5.1 (Microsoft.Testing.Platform) |
| Sistema operacional | Windows 11 (10.0.26300), x64 |
