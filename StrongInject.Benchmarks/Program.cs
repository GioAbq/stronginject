using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BenchmarkDotNet.Running;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace StrongInject.Benchmarks
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Stage 2: `dotnet run -c Release -- bench` runs the BenchmarkDotNet wall-clock comparison.
            // Default (no arg): Stage 1 deterministic re-compute / CS8785 diagnostic.
            if (args.Length > 0 && IsBenchArg(args[0]))
            {
                BenchmarkRunner.Run<GeneratorBenchmark>(GeneratorBenchmark.BuildConfig());
                return 0;
            }

            return Diagnostics.Run();
        }

        private static bool IsBenchArg(string arg)
            => arg.Equals("bench", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("bdn", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--benchmark", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deterministic comparison of the StrongInject 1.4.4 vs 2.0 incremental generators.
    /// Loads both builds into isolated AssemblyLoadContexts and measures, for each, how much work an
    /// edit triggers: a file with NO StrongInject code ("unrelated") and a single container edit.
    ///
    /// The regression signal is "how many generated outputs were RE-COMPUTED". We detect this by
    /// reference-equality of the produced <see cref="SourceText"/> objects: when the driver caches an
    /// output the prior instance is reused; when the callback re-runs a fresh instance is produced.
    /// This is robust to the quirks of TrackedOutputSteps and corroborated by wall-clock timing.
    /// </summary>
    internal static class Diagnostics
    {
        private const int TimingWarmup = 2;
        private const int TimingRepeats = 15;

        public static int Run()
        {
            Console.WriteLine("StrongInject generator incremental diagnostic (1.4.4 vs 2.0)");
            Console.WriteLine($"Synthetic project: {SyntheticProject.ContainerCount} containers, "
                + $"{SyntheticProject.ModuleCount} modules, shared chain depth {SyntheticProject.ModuleCount * SyntheticProject.ServicesPerModule}");
            Console.WriteLine();

            var rows = new List<Row>();
            foreach (var version in new[] { GeneratorVersion.V144, GeneratorVersion.V200 })
            {
                try
                {
                    rows.Add(Measure(version));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{version}] FAILED: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine(ex);
                    return 1;
                }
            }

            PrintTable(rows);
            return 0;
        }

        private static Row Measure(GeneratorVersion version)
        {
            GeneratorPaths.Verify(version);

            var alc = new GeneratorLoadContext(version);
            var generator = alc.CreateGenerator(version);

            var project = new SyntheticProject();
            var compilation = CompilationFactory.Create(version, project, out var trees);

            var baseDriver = CreateDriver(generator);

            // Cold run: full generation, establishes the driver cache and the baseline outputs.
            var warm = baseDriver.RunGenerators(compilation);
            var warmResult = warm.GetRunResult();
            var baseline = SourcesByHint(warmResult);

            // Correctness: the FINAL compilation (synthetic source + generated code) must be error-free,
            // otherwise the generator did partial/garbage work. (The pre-generation compilation has
            // expected CS0535 "container doesn't implement IContainer.Run" - that's what the generator adds.)
            var finalCompilation = compilation.AddSyntaxTrees(warmResult.GeneratedTrees);
            var finalErrors = finalCompilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
            var generatorErrors = warmResult.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

            // Two edits. Container trees keep the SAME objects, so only the edited tree differs -
            // exactly what an IDE keystroke produces.
            var unrelatedComp = compilation.ReplaceSyntaxTree(
                trees.Single(t => t.FilePath == SyntheticProject.UnrelatedPath),
                CompilationFactory.ParseFile(SyntheticProject.UnrelatedPath, project.UnrelatedSource(salt: 1)));

            var containerComp = compilation.ReplaceSyntaxTree(
                trees.Single(t => t.FilePath == SyntheticProject.ContainerPath(0)),
                CompilationFactory.ParseFile(SyntheticProject.ContainerPath(0), project.ContainerSource(0, extraMembers: 1)));

            // Regenerated-output counts (driver 'warm' is immutable -> reused as the baseline for both).
            var unrelatedResult = warm.RunGenerators(unrelatedComp).GetRunResult();
            var containerResult = warm.RunGenerators(containerComp).GetRunResult();
            var unrelatedRegen = CountRegenerated(baseline, unrelatedResult);
            var containerRegen = CountRegenerated(baseline, containerResult);
            var unrelatedSteps = StepHistogram(unrelatedResult);
            var containerSteps = StepHistogram(containerResult);
            var containerRunDiag = DescribeDiagnostics(containerResult);
            var unrelatedRunDiag = DescribeDiagnostics(unrelatedResult);

            // Orientation timings (median of repeats), each incremental edit from a fresh warm driver.
            var coldMs = MedianMs(() => CreateDriver(generator).RunGenerators(compilation));
            var unrelatedMs = MedianMs(() => Warm(generator, compilation).RunGenerators(unrelatedComp));
            var containerMs = MedianMs(() => Warm(generator, compilation).RunGenerators(containerComp));

            alc.Unload();

            return new Row
            {
                Version = version,
                FinalErrors = finalErrors,
                GeneratorErrors = generatorErrors,
                GeneratedFiles = baseline.Count,
                UnrelatedRegen = unrelatedRegen,
                ContainerRegen = containerRegen,
                UnrelatedSteps = unrelatedSteps,
                ContainerSteps = containerSteps,
                UnrelatedRunDiag = unrelatedRunDiag,
                ContainerRunDiag = containerRunDiag,
                ColdMs = coldMs,
                UnrelatedMs = unrelatedMs,
                ContainerMs = containerMs,
            };
        }

        private static GeneratorDriver CreateDriver(ISourceGenerator generator)
            => CSharpGeneratorDriver.Create(
                new[] { generator },
                parseOptions: CompilationFactory.ParseOptions,
                optionsProvider: null,
                additionalTexts: null,
                driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        private static GeneratorDriver Warm(ISourceGenerator generator, Compilation compilation)
            => CreateDriver(generator).RunGenerators(compilation);

        private static double MedianMs(Func<GeneratorDriver> action)
        {
            for (var i = 0; i < TimingWarmup; i++)
                action();

            var samples = new List<double>(TimingRepeats);
            for (var i = 0; i < TimingRepeats; i++)
            {
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            return samples[samples.Count / 2];
        }

        private static Dictionary<string, SourceText> SourcesByHint(GeneratorDriverRunResult result)
        {
            var map = new Dictionary<string, SourceText>(StringComparer.Ordinal);
            if (result.Results.Length == 0)
                return map;
            foreach (var src in result.Results[0].GeneratedSources)
                map[src.HintName] = src.SourceText;
            return map;
        }

        /// <summary>
        /// Counts how many generated outputs were RE-COMPUTED after an edit: a different
        /// <see cref="SourceText"/> instance for the same hint means the callback re-ran (no cache hit).
        /// </summary>
        private static int CountRegenerated(Dictionary<string, SourceText> baseline, GeneratorDriverRunResult after)
        {
            var regenerated = 0;
            foreach (var kvp in SourcesByHint(after))
            {
                if (!baseline.TryGetValue(kvp.Key, out var prior) || !ReferenceEquals(prior, kvp.Value))
                    regenerated++;
            }
            return regenerated;
        }

        private static string DescribeDiagnostics(GeneratorDriverRunResult result)
        {
            var diags = result.Diagnostics;
            if (diags.IsDefaultOrEmpty)
                return "none";
            return string.Join(" | ", diags
                .GroupBy(d => d.Id)
                .Select(g => $"{g.Key} x{g.Count()}: {g.First().GetMessage()}")
                .Take(3));
        }

        /// <summary>Per-step reason histogram from the incremental run, for understanding WHAT re-ran.</summary>
        private static string StepHistogram(GeneratorDriverRunResult result)
        {
            if (result.Results.Length == 0)
                return "    (no results)";
            var lines = new List<string>();
            foreach (var step in result.Results[0].TrackedSteps.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                var byReason = step.Value
                    .SelectMany(s => s.Outputs)
                    .GroupBy(o => o.Reason)
                    .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
                    .Select(g => $"{g.Key}={g.Count()}");
                lines.Add($"    {step.Key}: {string.Join(", ", byReason)}");
            }
            return lines.Count == 0 ? "    (no tracked steps)" : string.Join(Environment.NewLine, lines);
        }

        private static string Outcome(int regen, string runDiag)
            => runDiag.StartsWith("CS8785", StringComparison.Ordinal) ? "CRASH (CS8785)" : regen.ToString();

        private static void PrintTable(List<Row> rows)
        {
            Console.WriteLine("Sanity (per version):");
            foreach (var r in rows)
                Console.WriteLine($"  {r.Version,-5} final-compilation errors: {r.FinalErrors}, generator errors: {r.GeneratorErrors}, generated files: {r.GeneratedFiles}");
            Console.WriteLine();

            Console.WriteLine($"Generated outputs RE-COMPUTED after an edit (out of {rows[0].GeneratedFiles}; CRASH = generator threw):");
            Console.WriteLine($"  {"Version",-7} {"UnrelatedEdit",-16} {"ContainerEdit",-16}");
            foreach (var r in rows)
                Console.WriteLine($"  {r.Version,-7} {Outcome(r.UnrelatedRegen, r.UnrelatedRunDiag),-16} {Outcome(r.ContainerRegen, r.ContainerRunDiag),-16}");
            Console.WriteLine();

            Console.WriteLine($"Median time (ms), median of {TimingRepeats} runs (after {TimingWarmup} warmup):");
            Console.WriteLine($"  {"Version",-7} {"Cold",-10} {"UnrelatedEdit",-16} {"ContainerEdit",-16}");
            foreach (var r in rows)
                Console.WriteLine($"  {r.Version,-7} {r.ColdMs,-10:F2} {r.UnrelatedMs,-16:F2} {r.ContainerMs,-16:F2}");
            Console.WriteLine();

            Console.WriteLine("Interpretation: on an UNRELATED edit, 1.4.4 should re-compute ~0 outputs (CompilationWrapper");
            Console.WriteLine("cache) while 2.0 re-computes all of them (CompilationProvider combined directly). If so, 2.0");
            Console.WriteLine("regresses incremental generation - the main IDE use case.");
            Console.WriteLine();
            Console.WriteLine("=== Tracked-step reasons (diagnostic) ===");
            foreach (var r in rows)
            {
                Console.WriteLine($"[{r.Version}] UnrelatedEdit:");
                Console.WriteLine(r.UnrelatedSteps);
                Console.WriteLine($"[{r.Version}] ContainerEdit:");
                Console.WriteLine(r.ContainerSteps);
                Console.WriteLine($"    diagnostics(unrelated): {r.UnrelatedRunDiag}");
                Console.WriteLine($"    diagnostics(container): {r.ContainerRunDiag}");
            }
        }

        private sealed class Row
        {
            public GeneratorVersion Version;
            public int FinalErrors;
            public int GeneratorErrors;
            public int GeneratedFiles;
            public int UnrelatedRegen;
            public int ContainerRegen;
            public string UnrelatedSteps = "";
            public string ContainerSteps = "";
            public string UnrelatedRunDiag = "";
            public string ContainerRunDiag = "";
            public double ColdMs;
            public double UnrelatedMs;
            public double ContainerMs;
        }
    }
}
