using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// Stage 2: rigorous wall-clock comparison of the StrongInject 1.4.4 vs 2.0 incremental generators,
    /// measured with BenchmarkDotNet's statistical engine (warmup, multiple iterations, outlier removal).
    /// This complements <see cref="Diagnostics"/> (Stage 1), which proves the deterministic re-compute
    /// counts; here we put real time + allocation numbers on those three scenarios.
    ///
    /// Each (Version) value is a separate benchmark case. <see cref="Setup"/> builds an isolated
    /// <see cref="GeneratorLoadContext"/>, the synthetic compilation, a warmed driver, and the two edited
    /// compilations; <see cref="Cleanup"/> unloads the context. The benchmark methods mirror the Stage 1
    /// scenarios exactly so the numbers line up with the determinstic counts in the report.
    /// </summary>
    [MemoryDiagnoser]
    public class GeneratorBenchmark
    {
        [Params(GeneratorVersion.V144, GeneratorVersion.V200)]
        public GeneratorVersion Version;

        private GeneratorLoadContext _alc;
        private ISourceGenerator _generator;
        private CSharpCompilation _compilation;
        private GeneratorDriver _warm;
        private CSharpCompilation _unrelatedComp;
        private CSharpCompilation _containerComp;

        [GlobalSetup]
        public void Setup()
        {
            GeneratorPaths.Verify(Version);
            _alc = new GeneratorLoadContext(Version);
            _generator = _alc.CreateGenerator(Version);

            var project = new SyntheticProject();
            _compilation = CompilationFactory.Create(Version, project, out var trees);

            // Warmed driver: full cold generation once, so UnrelatedEdit/ContainerEdit measure incremental work.
            _warm = CreateDriver(_generator).RunGenerators(_compilation);

            // Single-tree edits, same object identity for every other tree - exactly an IDE keystroke.
            _unrelatedComp = _compilation.ReplaceSyntaxTree(
                trees.Single(t => t.FilePath == SyntheticProject.UnrelatedPath),
                CompilationFactory.ParseFile(SyntheticProject.UnrelatedPath, project.UnrelatedSource(salt: 1)));

            _containerComp = _compilation.ReplaceSyntaxTree(
                trees.Single(t => t.FilePath == SyntheticProject.ContainerPath(0)),
                CompilationFactory.ParseFile(SyntheticProject.ContainerPath(0), project.ContainerSource(0, extraMembers: 1)));
        }

        [GlobalCleanup]
        public void Cleanup() => _alc?.Unload();

        /// <summary>Full generation from a fresh driver (no incremental cache) - the build-from-scratch path.</summary>
        [Benchmark]
        public GeneratorDriver Cold() => CreateDriver(_generator).RunGenerators(_compilation);

        /// <summary>Edit of a file with NO StrongInject code. 1.4.4 caches (~0 work); 2.0 (pre-fix) regenerated all.</summary>
        [Benchmark]
        public GeneratorDriver UnrelatedEdit() => _warm.RunGenerators(_unrelatedComp);

        /// <summary>Edit of one container body. 2.0 (pre-fix) threw CS8785 here; both should now recompute exactly 1.</summary>
        [Benchmark]
        public GeneratorDriver ContainerEdit() => _warm.RunGenerators(_containerComp);

        private static GeneratorDriver CreateDriver(ISourceGenerator generator)
            => CSharpGeneratorDriver.Create(
                new[] { generator },
                parseOptions: CompilationFactory.ParseOptions,
                optionsProvider: null,
                additionalTexts: null,
                driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        /// <summary>
        /// In-process toolchain config (see csproj comment for why not the default child-process toolchain).
        /// Built on top of <see cref="DefaultConfig"/> so we keep its columns/loggers/exporters and supply
        /// exactly one job - the in-process one - instead of the implicit default child-process job.
        /// </summary>
        public static IConfig BuildConfig()
            => ManualConfig.Create(DefaultConfig.Instance)
                .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
    }
}
