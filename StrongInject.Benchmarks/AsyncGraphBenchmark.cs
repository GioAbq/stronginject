using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// Shape-axis benchmark: cold generation of an async dependency lattice (<see cref="AsyncLatticeProject"/>)
    /// swept over <see cref="Depth"/>. Unlike <see cref="GeneratorBenchmark"/> (which fixes the shape and
    /// varies the generator Version), this fixes the generator at V200 and varies the GRAPH SHAPE, so it
    /// can surface algorithmic cliffs that a single fixed shape hides - here specifically the async-only
    /// un-memoized <c>LoweringVisitor.FindLongestPath</c> DFS (paths grow as Width^Depth).
    ///
    /// Run with <c>dotnet run -c Release -- bench-async</c>.
    /// </summary>
    [MemoryDiagnoser]
    public class AsyncGraphBenchmark
    {
        /// <summary>Depth axis: each +step multiplies the distinct-path count by <see cref="Width"/>.</summary>
        [Params(8, 12, 16, 20)]
        public int Depth;

        /// <summary>Branching factor (fan-out per layer). Width^Depth = distinct root-to-leaf paths.</summary>
        public int Width = 2;

        private GeneratorLoadContext _alc;
        private ISourceGenerator _generator;
        private CSharpCompilation _compilation;

        [GlobalSetup]
        public void Setup()
        {
            const GeneratorVersion version = GeneratorVersion.V200;
            GeneratorPaths.Verify(version);
            _alc = new GeneratorLoadContext(version);
            _generator = _alc.CreateGenerator(version);
            // netstandard2.1 runtime so the async container's IAsyncDisposable resolves (see StrongInjectDll21).
            _compilation = CompilationFactory.Create(GeneratorPaths.StrongInjectDll21(version), new AsyncLatticeProject(Width, Depth).Files(), out _);
        }

        [GlobalCleanup]
        public void Cleanup() => _alc?.Unload();

        /// <summary>Full cold generation of the async lattice - exercises the async ordering pass.</summary>
        [Benchmark]
        public GeneratorDriver ColdAsyncLattice() => CreateDriver(_generator).RunGenerators(_compilation);

        private static GeneratorDriver CreateDriver(ISourceGenerator generator)
            => CSharpGeneratorDriver.Create(
                new[] { generator },
                parseOptions: CompilationFactory.ParseOptions,
                optionsProvider: null,
                additionalTexts: null,
                driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        public static IConfig BuildConfig()
            => ManualConfig.Create(DefaultConfig.Instance)
                .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
    }
}
