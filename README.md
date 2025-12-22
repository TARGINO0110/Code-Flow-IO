# Code-Flow-IO

## Description

Code-Flow-IO is a tool that generates flowcharts from the C# source code of a .NET solution.  
It uses Roslyn to analyze the Control Flow Graph (CFG) of each implemented method and converts that flow into [Mermaid](https://mermaid-js.github.io/) diagrams, automatically producing `.mmd`, `.svg`, and `.png` files.

## How it works

1. **Solution reading:**  
   The tool opens a `.sln` specified by the user.

2. **Compilation and analysis:**  
   For each project (or a filtered project) it compiles and analyzes all `.cs` files.

3. **CFG generation:**  
   For each implemented method (with a body), the tool generates the Control Flow Graph using Roslyn.

4. **Conversion to Mermaid:**  
   The CFG is converted into a Mermaid diagram (`.mmd`).

5. **Diagram rendering:**  
   The [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli) (`mmdc`) is used to produce `.svg` and `.png` files from the `.mmd`.

6. **Output files:**  
   Generated files are saved to the specified output directory, overwriting previous versions.

7. **Code Base**
   Program.cs

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/)
- [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli) installed globally

## Install Mermaid CLI

To render `.svg` and `.png` files you must install the Mermaid CLI on your machine.

**Install Mermaid CLI globally with:**

`npm install -g @mermaid-js/mermaid-cli`

After installation, the `mmdc` command will be available in the terminal and will be used automatically by the tool to convert `.mmd` files into `.svg` and `.png`.

**Verify installation:**

`mmdc -h`

If the Mermaid CLI help appears, the installation is correct.

## Project installation

Clone the repository and restore dependencies:

`git clone <repository-url> && cd Code-Flow-IO && dotnet restore`

## How to run

Run the command below, providing the solution path and output directory:

`dotnet run --project src/Rest.Code-Flow-io/Rest.Code-Flow-io.csproj -- <path.sln> <out-dir>`

Example:

`dotnet run --project src/Rest.Code-Flow-io/Rest.Code-Flow-io.csproj -- C:\projects\my-sln.sln docs/flow/mmd`

To filter by a specific project:

`dotnet run --project src/Rest.Code-Flow-io/Rest.Code-Flow-io.csproj -- C:\projects\my-sln.sln docs/flow/mmd --project ProjectName`

## Generated files structure

- `<out-dir>/<Project>_<File>_<Method>.mmd`  (Mermaid diagram)
- `<out-dir>/<Project>_<File>_<Method>.svg`  (SVG image)
- `<out-dir>/<Project>_<File>_<Method>.png`  (PNG image)

Files are overwritten on each run.

## Notes

- Only methods with bodies are processed (interfaces and abstract methods are ignored).
- The Mermaid CLI must be available in the system PATH.
- If any generated `.mmd` file causes a syntax error, inspect the `.mmd` content and adjust the source or sanitization logic as needed.
- The program prints warnings with details when diagram generation fails.

## References

- [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli)
- [Mermaid Live Editor](https://mermaid.live/)
- [Roslyn Flow Analysis](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/control-flow-graph)