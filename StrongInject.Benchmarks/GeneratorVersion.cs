using System.IO;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// The two StrongInject generator builds we compare. Both expose the same type
    /// <c>StrongInject.Generator.IncrementalGenerator</c> implementing <c>IIncrementalGenerator</c>,
    /// so they can only coexist in one process when loaded into separate AssemblyLoadContexts.
    /// </summary>
    public enum GeneratorVersion
    {
        /// <summary>1.4.4 (branch <c>main</c>) - uses the <c>CompilationWrapper</c> cache trick.</summary>
        V144,

        /// <summary>2.0 (branch <c>feature/stronginject-2.0</c>) - combines <c>CompilationProvider</c> directly.</summary>
        V200,
    }

    /// <summary>
    /// Absolute paths to the packaged generator/runtime DLLs in the global NuGet cache (X:\N).
    /// Verified present on disk. These are intentionally machine-local: this is a dev-only
    /// benchmark that compares the actual shipped analyzer artifacts.
    /// </summary>
    public static class GeneratorPaths
    {
        private const string Cache = @"X:\N\stronginject";

        // Per-version environment overrides, e.g. SI_V200_ROSLYN40 / SI_V200_GENERATOR / SI_V200_STRONGINJECT.
        // Used to point the harness at a freshly built generator (bin/Release) when validating a fix,
        // without the pack-and-restore-into-X:\N dance.
        private static string Env(GeneratorVersion version, string suffix)
            => System.Environment.GetEnvironmentVariable($"SI_{version}_{suffix}");

        public static string Roslyn40Dll(GeneratorVersion version) => Env(version, "ROSLYN40") ?? (version switch
        {
            GeneratorVersion.V144 => Path.Combine(Cache, @"1.4.4\analyzers\dotnet\roslyn4.0\cs\StrongInject.Generator.Roslyn40.dll"),
            GeneratorVersion.V200 => Path.Combine(Cache, @"2.0.0-local\analyzers\dotnet\cs\StrongInject.Generator.Roslyn40.dll"),
            _ => throw new System.ArgumentOutOfRangeException(nameof(version)),
        });

        public static string GeneratorDll(GeneratorVersion version) => Env(version, "GENERATOR") ?? (version switch
        {
            GeneratorVersion.V144 => Path.Combine(Cache, @"1.4.4\analyzers\dotnet\cs\StrongInject.Generator.dll"),
            GeneratorVersion.V200 => Path.Combine(Cache, @"2.0.0-local\analyzers\dotnet\cs\StrongInject.Generator.dll"),
            _ => throw new System.ArgumentOutOfRangeException(nameof(version)),
        });

        /// <summary>The StrongInject runtime assembly (attributes + IContainer) referenced by the synthetic compilation.</summary>
        public static string StrongInjectDll(GeneratorVersion version) => Env(version, "STRONGINJECT") ?? (version switch
        {
            GeneratorVersion.V144 => Path.Combine(Cache, @"1.4.4\lib\netstandard2.0\StrongInject.dll"),
            GeneratorVersion.V200 => Path.Combine(Cache, @"2.0.0-local\lib\netstandard2.0\StrongInject.dll"),
            _ => throw new System.ArgumentOutOfRangeException(nameof(version)),
        });

        /// <summary>Throws a clear error if any expected DLL is missing (e.g. cache cleared).</summary>
        public static void Verify(GeneratorVersion version)
        {
            foreach (var path in new[] { Roslyn40Dll(version), GeneratorDll(version), StrongInjectDll(version) })
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Required generator artifact for {version} not found. Re-pack the local package or restore the 1.4.4 cache.", path);
            }
        }
    }
}
