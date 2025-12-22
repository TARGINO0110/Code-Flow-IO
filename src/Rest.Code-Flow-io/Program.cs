using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Rest.Code_Flow_io
{
    [ExcludeFromCodeCoverage]
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: cfg2mmd <path.sln> <out-dir> [--project ProjectName]");
                return 1;
            }

            var slnPath = args[0];
            var outDir = args[1];
            string? onlyProject = null;

            if (args.Length >= 3 && args[2] == "--project")
            {
                if (args.Length < 4)
                {
                    Console.Error.WriteLine("Error: missing project name after --project.");
                    return 2;
                }
                onlyProject = args[3];
            }

            Directory.CreateDirectory(outDir);

            // Build configuration (appsettings.json optional)
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            // Read limits with sensible defaults
            int maxDocsPerProject = config.GetValue<int?>("Architecture:MaxDocsPerProject") ?? 8;
            int maxClassesPerProject = config.GetValue<int?>("Architecture:MaxClassesPerProject") ?? 10;
            int maxMethodsPerClass = config.GetValue<int?>("Architecture:MaxMethodsPerClass") ?? 10;
            int maxFreeMethods = config.GetValue<int?>("Architecture:MaxFreeMethods") ?? 10;

            Console.WriteLine($"Architecture limits: Docs={maxDocsPerProject}, Classes={maxClassesPerProject}, MethodsPerClass={maxMethodsPerClass}, FreeMethods={maxFreeMethods}");

            try
            {
                MSBuildLocator.RegisterDefaults();

                using var workspace = MSBuildWorkspace.Create();
                workspace.RegisterWorkspaceFailedHandler(e => Console.Error.WriteLine($"[Workspace] {e.Diagnostic}"));

                Console.WriteLine($"Opening solution: {slnPath}");
                var solution = await workspace.OpenSolutionAsync(slnPath);

                foreach (var project in solution.Projects.Where(p => onlyProject is null || p.Name == onlyProject))
                {
                    Console.WriteLine($"Project: {project.Name}");
                    var compilation = await project.GetCompilationAsync();
                    if (compilation is null)
                    {
                        Console.Error.WriteLine($"[WARN] Could not compile {project.Name}");
                        continue;
                    }

                    foreach (var document in project.Documents.Where(d => d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                    {
                        var tree = await document.GetSyntaxTreeAsync();
                        var root = await document.GetSyntaxRootAsync();
                        if (tree is null || root is null) continue;

                        var model = compilation.GetSemanticModel(tree);

                        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Body != null || m.ExpressionBody != null);
                        foreach (var method in methods)
                        {
                            try
                            {
                                var cfg = ControlFlowGraph.Create(method, model);

                                if (cfg is null)
                                {
                                    Console.Error.WriteLine($"[WARN] Could not create CFG for {document.FilePath}::{method.Identifier}");
                                    continue;
                                }

                                var mmd = BuildMermaid(cfg, project.Name, document.Name, method);

                                var safeName =
                                    $"{project.Name}_{Path.GetFileNameWithoutExtension(document.Name)}_{method.Identifier.Text}"
                                    .Replace(' ', '_')
                                    .Replace('.', '_');

                                var path = Path.Combine(outDir, safeName + ".mmd");

                                // Remove old files before generating new ones
                                DeleteIfExists(path);
                                DeleteIfExists(Path.ChangeExtension(path, "svg"));
                                DeleteIfExists(Path.ChangeExtension(path, "png"));

                                await File.WriteAllTextAsync(path, mmd, Encoding.UTF8);
                                Console.WriteLine($"Generated: {path}");

                                // Generate SVG
                                if (await GenerateMermaidImage(path, "svg"))
                                    Console.WriteLine($"Generated: {Path.ChangeExtension(path, "svg")}");
                                else
                                    Console.Error.WriteLine($"[WARN] Failed to generate SVG for {path}");

                                // Generate PNG
                                if (await GenerateMermaidImage(path, "png"))
                                    Console.WriteLine($"Generated: {Path.ChangeExtension(path, "png")}");
                                else
                                    Console.Error.WriteLine($"[WARN] Failed to generate PNG for {path}");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[WARN] Failed to generate CFG for {document.FilePath}::{method.Identifier}: {ex.Message}");
                            }
                        }
                    }
                }

                // Generate a topology / architecture file that interlinks projects, docs and the processing chain
                await GenerateArchitectureDocuments(solution, outDir,
                    maxDocsPerProject, maxClassesPerProject, maxMethodsPerClass, maxFreeMethods);

                Console.WriteLine("Done.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
                return 99;
            }
        }

        /// <summary>
        /// Calls the Mermaid CLI (mmdc) to generate SVG/PNG from the .mmd file.
        /// </summary>
        private static async Task<bool> GenerateMermaidImage(string mmdPath, string format)
        {
            var outputPath = Path.ChangeExtension(mmdPath, format);

            string? mmdcPath = "mmdc";
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Look for mmdc.cmd on the PATH
                var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
                foreach (var p in paths)
                {
                    var candidate = Path.Combine(p.Trim(), "mmdc.cmd");
                    if (File.Exists(candidate))
                    {
                        mmdcPath = candidate;
                        break;
                    }
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = mmdcPath,
                Arguments = $"-i \"{mmdPath}\" -o \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;
            string stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                Console.Error.WriteLine($"[MMDC ERROR] {stdErr}");
            return process.ExitCode == 0;

        }



        /// <summary>
        /// Converts a ControlFlowGraph to a Mermaid flowchart.
        /// </summary>
        private static string BuildMermaid(ControlFlowGraph cfg, string project, string docName, MethodDeclarationSyntax method)
        {
            var sb = new StringBuilder();

            sb.AppendLine("%% Diagram generated automatically (CFG -> Mermaid)");
            sb.AppendLine($"%% Project: {project} | File: {docName} | Method: {method.Identifier.Text}");
            sb.AppendLine("flowchart TD");

            for (int i = 0; i < cfg.Blocks.Length; i++)
            {
                var block = cfg.Blocks[i];
                var id = $"B{i}";

                // use <br/> so Mermaid renders line breaks properly (prevents showing '\n' in PNG)
                var ops = string.Join("<br/>",
                    block.Operations
                         .Select(op => op.Syntax?.ToString()?.Trim())
                         .Where(s => !string.IsNullOrWhiteSpace(s)));

                var label = string.IsNullOrWhiteSpace(ops) ? $"Block {i}" : $"Block {i}<br/>{Escape(ops)}";
                sb.AppendLine($"{id}[\"{label}\"]");

                if (block.BranchValue is not null)
                {
                    var condText = Escape(block.BranchValue.Syntax?.ToString() ?? "cond");
                    var did = $"D{i}";
                    // use brackets for consistency and to avoid parser ambiguities
                    sb.AppendLine($"{did}[\"{condText}\"]");
                    sb.AppendLine($"{id} --> {did}");

                    var condDest = BlockId(cfg, block.ConditionalSuccessor?.Destination);
                    var fallDest = BlockId(cfg, block.FallThroughSuccessor?.Destination);

                    if (block.ConditionKind == ControlFlowConditionKind.WhenTrue)
                    {
                        if (condDest != "END") sb.AppendLine($"{did} -- \"true\" --> {condDest}");
                        if (fallDest != "END") sb.AppendLine($"{did} -- \"false\" --> {fallDest}");
                    }
                    else if (block.ConditionKind == ControlFlowConditionKind.WhenFalse)
                    {
                        if (fallDest != "END") sb.AppendLine($"{did} -- \"true\" --> {fallDest}");
                        if (condDest != "END") sb.AppendLine($"{did} -- \"false\" --> {condDest}");
                    }
                    else
                    {
                        if (fallDest != "END") sb.AppendLine($"{did} --> {fallDest}");
                    }
                }
                else
                {
                    if (block.FallThroughSuccessor is not null)
                    {
                        sb.AppendLine($"{id} --> {BlockId(cfg, block.FallThroughSuccessor.Destination)}");
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the textual ID of the destination block (B#), or "END" if null.
        /// </summary>
        private static string BlockId(ControlFlowGraph cfg, BasicBlock? dest)
        {
            if (dest is null) return "END";
            var idx = cfg.Blocks.IndexOf(dest);
            return idx >= 0 ? $"B{idx}" : "END";
        }

        /// <summary>
        /// Escapes characters that break Mermaid syntax (quotes and line breaks).
        /// </summary>
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Replace("\"", "'")
                    .Replace("\\", "/")
                    .Replace("\r", " ")
                    // map newlines to <br/> (Mermaid renders HTML line breaks correctly)
                    .Replace("\n", "<br/>");
        }

        private static void DeleteIfExists(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Could not delete {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates architecture documentation (markdown + .mmd) describing the processing chain
        /// and the list of projects/documents scanned. Also tries to render a SVG/PNG via mmdc.
        /// Now includes classes and methods (limited) per project/document for readability.
        /// </summary>
        private static async Task GenerateArchitectureDocuments(Solution solution, string outDir,
            int maxDocsPerProject, int maxClassesPerProject, int maxMethodsPerClass, int maxFreeMethods)
        {
            try
            {
                var docsDir = Path.Combine(outDir, "docs-architecture");
                Directory.CreateDirectory(docsDir);

                // Build mermaid diagram
                var mmdSb = new StringBuilder();
                mmdSb.AppendLine("flowchart TD");
                mmdSb.AppendLine("  P[\"Program (entry)\"] --> ML[\"MSBuildLocator\"]");
                mmdSb.AppendLine("  ML --> WS[\"MSBuildWorkspace\"]");
                mmdSb.AppendLine("  WS --> SOL[\"Solution\"]");

                int pIdx = 0;

                foreach (var project in solution.Projects)
                {
                    var pid = $"PRJ{pIdx}";
                    var safeProjectName = Escape(project.Name).Replace(" ", "_");
                    mmdSb.AppendLine($"  SOL --> {pid}[\"Project: {safeProjectName}\"]");
                    mmdSb.AppendLine($"  {pid} --> DOC{pIdx}[\"Documents (.cs)\"]");

                    var docs = project.Documents.Take(maxDocsPerProject).ToList();
                    for (int d = 0; d < docs.Count; d++)
                    {
                        var doc = docs[d];
                        var docNode = $"PRJ{pIdx}D{d}";
                        var docName = Path.GetFileName(doc.Name);
                        mmdSb.AppendLine($"  DOC{pIdx} --> {docNode}[\"{Escape(docName)}\"]");
                    }

                    // try to get compilation to extract types/methods
                    var compilation = await project.GetCompilationAsync();
                    if (compilation is not null)
                    {
                        var classCount = 0;
                        var freeMethodCount = 0;

                        // iterate documents (limited) to extract type and method information
                        foreach (var doc in project.Documents.Take(maxDocsPerProject))
                        {
                            var root = await doc.GetSyntaxRootAsync();
                            if (root is null) continue;

                            // classes/structs/records
                            var types = root.DescendantNodes().Where(n =>
                                        n is ClassDeclarationSyntax || n is StructDeclarationSyntax || n is RecordDeclarationSyntax)
                                        .Cast<TypeDeclarationSyntax?>()
                                        .Where(t => t is not null)
                                        .Select(t => t!).ToList();

                            foreach (var t in types)
                            {
                                if (classCount >= maxClassesPerProject) break;
                                var classNode = $"PRJ{pIdx}C{classCount}";
                                var className = ((TypeDeclarationSyntax)t).Identifier.Text;
                                mmdSb.AppendLine($"  {pid} --> {classNode}[\"Class: {Escape(className)}\"]");
                                mmdSb.AppendLine($"  {classNode} --> DOC{pIdx}"); // link to docs group for context

                                // methods inside this class
                                var methods = t.DescendantNodes().OfType<MethodDeclarationSyntax>()
                                               .Where(m => m.Body != null || m.ExpressionBody != null)
                                               .Take(maxMethodsPerClass)
                                               .ToList();

                                for (int mi = 0; mi < methods.Count; mi++)
                                {
                                    var mnode = $"PRJ{pIdx}C{classCount}M{mi}";
                                    var mname = methods[mi].Identifier.Text;
                                    mmdSb.AppendLine($"  {classNode} --> {mnode}[\"Method: {Escape(mname)}()\"]");
                                }

                                classCount++;
                            }

                            // top-level methods (e.g., in scripts or partials not inside type) - limited
                            var topMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                                            .Where(m =>
                                            {
                                                // method whose nearest ancestor that is a TypeDeclarationSyntax is null => top-level
                                                return m.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() == null;
                                            }).Take(maxFreeMethods).ToList();

                            foreach (var tm in topMethods)
                            {
                                if (freeMethodCount >= maxFreeMethods) break;
                                var mnode = $"PRJ{pIdx}FM{freeMethodCount}";
                                var mname = tm.Identifier.Text;
                                mmdSb.AppendLine($"  {pid} --> {mnode}[\"TopMethod: {Escape(mname)}()\"]");
                                freeMethodCount++;
                            }
                        }
                    }

                    // show the per-project pipeline
                    mmdSb.AppendLine($"  PRJ{pIdx} --> CFG{pIdx}[\"ControlFlowGraph (per-method)\"]");
                    mmdSb.AppendLine($"  CFG{pIdx} --> BM{pIdx}[\"BuildMermaid()\"]");
                    mmdSb.AppendLine($"  BM{pIdx} --> MMD{pIdx}[\"{Escape(project.Name)}.mmd\"]");
                    mmdSb.AppendLine($"  MMD{pIdx} --> MMDC{pIdx}[\"Mermaid CLI (mmdc)\"]");
                    mmdSb.AppendLine($"  MMDC{pIdx} --> OUT{pIdx}[\".svg / .png\"]");

                    pIdx++;
                }

                mmdSb.AppendLine("  P --> DEL[\"DeleteIfExists() (cleanup)\"]");
                mmdSb.AppendLine("  P --> GEN[\"GenerateMermaidImage() (calls mmdc)\"]");

                var mmdContent = mmdSb.ToString();

                // Markdown wrapper
                var mdSb = new StringBuilder();
                mdSb.AppendLine("# Architecture — Code-Flow-IO");
                mdSb.AppendLine();
                mdSb.AppendLine("This file describes the processing chain and the projects/documents scanned by the tool.");
                mdSb.AppendLine();
                mdSb.AppendLine("## Projects scanned");
                foreach (var proj in solution.Projects)
                {
                    mdSb.AppendLine($"- {proj.Name}");
                }
                mdSb.AppendLine();
                mdSb.AppendLine("## Flow diagram (Mermaid)");
                mdSb.AppendLine();
                mdSb.AppendLine("```mermaid");
                mdSb.AppendLine(mmdContent);
                mdSb.AppendLine("```");
                mdSb.AppendLine();
                mdSb.AppendLine("*Generated automatically by the tool.*");

                var mdPath = Path.Combine(docsDir, "ARCHITECTURE.md");
                var mmdPath = Path.Combine(docsDir, "ARCHITECTURE.mmd");

                await File.WriteAllTextAsync(mdPath, mdSb.ToString(), Encoding.UTF8);
                await File.WriteAllTextAsync(mmdPath, mmdContent, Encoding.UTF8);

                Console.WriteLine($"Architecture docs generated: {mdPath} and {mmdPath}");

                // Try to render the diagram images (best-effort)
                if (await GenerateMermaidImage(mmdPath, "svg"))
                    Console.WriteLine($"Architecture SVG generated: {Path.ChangeExtension(mmdPath, "svg")}");
                if (await GenerateMermaidImage(mmdPath, "png"))
                    Console.WriteLine($"Architecture PNG generated: {Path.ChangeExtension(mmdPath, "png")}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to generate architecture documents: {ex.Message}");
            }
        }
    }
}