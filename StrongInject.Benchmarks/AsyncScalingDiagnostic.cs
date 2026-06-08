using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// Deterministic depth-sweep that turns the suspected <c>LoweringVisitor.FindLongestPath</c> cliff
    /// (async-only, un-memoized DFS) into a measured curve. For each depth it builds an
    /// <see cref="AsyncLatticeProject"/> (Width^Depth distinct paths), verifies the generator produced
    /// error-free async code (so we time real lowering work, not an early bail), and records a single
    /// cold-generation wall time. Runs V200 only - this is about absolute scaling of the current
    /// generator, not a 1.4.4-vs-2.0 comparison. Each row is flushed to a results file as it completes,
    /// so a partial curve survives even if a deep run is killed.
    ///
    /// Usage: <c>dotnet run -c Release -- async [width] [depth...]</c>
    /// (default: width 2, depths 8 12 16 18 20 22).
    /// </summary>
    internal static class AsyncScalingDiagnostic
    {
        public static int Run(int width, int[] depths)
        {
            const GeneratorVersion version = GeneratorVersion.V200;
            GeneratorPaths.Verify(version);

            // Write into the (git-ignored) BenchmarkDotNet.Artifacts folder so a partial curve survives a
            // killed deep run without dirtying the working tree.
            var artifactsDir = Path.Combine(Environment.CurrentDirectory, "BenchmarkDotNet.Artifacts");
            Directory.CreateDirectory(artifactsDir);
            var resultsPath = Path.Combine(artifactsDir, "async-scaling-results.txt");
            using var fw = new StreamWriter(resultsPath, append: false);

            void Emit(string line)
            {
                Console.WriteLine(line);
                Console.Out.Flush();
                fw.WriteLine(line);
                fw.Flush();
            }

            Emit($"Async lattice scaling (V200, width={width}) - targets LoweringVisitor.Order/FindLongestPath");
            Emit($"{"depth",-6} {"nodes",-7} {"paths~",-12} {"coldMs",-12} {"genErr",-7} {"finalErr",-8} {"genFiles",-9}");

            // Async scenarios need the netstandard2.1 runtime so IAsyncDisposable resolves (see StrongInjectDll21).
            var strongInjectDll = GeneratorPaths.StrongInjectDll21(version);
            var alc = new GeneratorLoadContext(version);
            var generator = alc.CreateGenerator(version);
            try
            {
                // One JIT warmup on a trivial lattice so the first real row isn't skewed by first-run JIT.
                CreateDriver(generator).RunGenerators(
                    CompilationFactory.Create(strongInjectDll, new AsyncLatticeProject(width, 2).Files(), out _));

                foreach (var depth in depths)
                {
                    var project = new AsyncLatticeProject(width, depth);
                    var compilation = CompilationFactory.Create(strongInjectDll, project.Files(), out _);

                    var sw = Stopwatch.StartNew();
                    var run = CreateDriver(generator).RunGenerators(compilation);
                    sw.Stop();

                    var result = run.GetRunResult();
                    var genErr = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                    var finalErrors = compilation.AddSyntaxTrees(result.GeneratedTrees)
                        .GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                    var finalErr = finalErrors.Count;
                    var genFiles = result.Results.Length == 0 ? 0 : result.Results[0].GeneratedSources.Length;
                    var paths = Math.Pow(width, depth);

                    Emit($"{depth,-6} {project.NodeCount,-7} {paths,-12:G3} {sw.Elapsed.TotalMilliseconds,-12:F1} {genErr,-7} {finalErr,-8} {genFiles,-9}");

                    if (genErr > 0)
                        Emit($"       ^ generator reported {genErr} error(s): {Describe(result)}");
                    if (finalErr > 0)
                        Emit("       ^ finalErr: " + string.Join(" | ", finalErrors
                            .GroupBy(d => d.Id).Select(g => $"{g.Key} x{g.Count()}: {g.First().GetMessage()}").Take(4)));
                }
            }
            finally
            {
                alc.Unload();
            }

            Emit($"Done. Results: {resultsPath}");
            Emit("A super-linear coldMs curve (roughly tracking paths~ = width^depth) confirms the un-memoized");
            Emit("FindLongestPath DFS is the cliff; a flat/linear curve means the shape does not trigger it.");
            return 0;
        }

        private static string Describe(GeneratorDriverRunResult result)
            => result.Diagnostics.IsDefaultOrEmpty
                ? "none"
                : string.Join(" | ", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .GroupBy(d => d.Id)
                    .Select(g => $"{g.Key} x{g.Count()}: {g.First().GetMessage()}")
                    .Take(3));

        private static GeneratorDriver CreateDriver(ISourceGenerator generator)
            => CSharpGeneratorDriver.Create(
                new[] { generator },
                parseOptions: CompilationFactory.ParseOptions,
                optionsProvider: null,
                additionalTexts: null,
                driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));
    }
}
