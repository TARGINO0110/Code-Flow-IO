# Code-Flow-IO

## Descrição

Code-Flow-IO é uma ferramenta que gera fluxogramas a partir do código-fonte C# de uma solução .NET.  
Utiliza o Roslyn para analisar o Control Flow Graph (CFG) de cada método implementado e converte esse fluxo em diagramas [Mermaid](https://mermaid-js.github.io/), gerando arquivos `.mmd`, `.svg` e `.png` automaticamente.

## Funcionamento

1. **Leitura da solução:**  
   O programa abre uma solução `.sln` informada pelo usuário.

2. **Compilação e análise:**  
   Para cada projeto (ou projeto filtrado), compila e analisa todos os arquivos `.cs`.

3. **Geração do CFG:**  
   Para cada método implementado (com corpo), gera o Control Flow Graph usando Roslyn.

4. **Conversão para Mermaid:**  
   O CFG é convertido em um diagrama Mermaid (`.mmd`).

5. **Renderização dos diagramas:**  
   Utiliza o [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli) (`mmdc`) para gerar os arquivos `.svg` e `.png` a partir do `.mmd`.

6. **Arquivos gerados:**  
   Os arquivos são salvos no diretório de saída especificado, sobrescrevendo versões anteriores.

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/)
- [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli) instalado globalmente

## Instalação do Mermaid CLI

Para que a geração dos arquivos `.svg` e `.png` funcione, é necessário instalar o Mermaid CLI na sua máquina.

**Instale o Mermaid CLI globalmente com o comando:**

`npm install -g @mermaid-js/mermaid-cli`


Após a instalação, o comando `mmdc` estará disponível no terminal e será utilizado automaticamente pelo programa para converter os arquivos `.mmd` em `.svg` e `.png`.

**Verifique a instalação:**

`mmdc -h` 


Se aparecer a ajuda do Mermaid CLI, está instalado corretamente.

## Instalação do projeto

Clone o repositório e instale as dependências:

`git clone <url-do-repositorio> cd Code-Flow-IO dotnet restore`


## Como executar

Execute o comando abaixo, informando o caminho da solução e o diretório de saída:

`dotnet run --project src/Rest.Code-Flow-io/Rest.Code-Flow-io.csproj -- <caminho.sln> <dir-saida>`


Exemplo:

`dotnet run --project src/Rest.Code-Flow-io/Rest.Code-Flow-io.csproj -- C:\projetos\minha-sln.sln docs/flow/mmd`


Para filtrar por um projeto específico:

`dotnet run --project src/Rest.Code-Flow-io/Rest.Code-Flow-io.csproj -- C:\projetos\minha-sln.sln docs/flow/mmd --project NomeDoProjeto`


## Estrutura dos arquivos gerados

- `<dir-saida>/<Projeto>_<Arquivo>_<Metodo>.mmd`  (diagrama Mermaid)
- `<dir-saida>/<Projeto>_<Arquivo>_<Metodo>.svg`  (imagem SVG)
- `<dir-saida>/<Projeto>_<Arquivo>_<Metodo>.png`  (imagem PNG)

Os arquivos são sobrescritos a cada execução.

## Observações

- Apenas métodos com corpo são processados (interfaces e métodos abstratos são ignorados).
- O Mermaid CLI precisa estar disponível no PATH do sistema.
- Se algum arquivo `.mmd` gerar erro de sintaxe, verifique o conteúdo e ajuste o código conforme necessário.
- O programa exibe avisos detalhados em caso de falha na geração dos diagramas.

## Referências

- [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli)
- [Mermaid Live Editor](https://mermaid.live/)
- [Roslyn Flow Analysis](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/control-flow-graph)