using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// Loads one StrongInject generator build in isolation. The two builds share assembly names
    /// (<c>StrongInject.Generator</c>, <c>StrongInject.Generator.Roslyn40</c>) and file version
    /// 1.0.0.0, so the default context can hold only one - they must live in separate contexts.
    ///
    /// Crucially, every Roslyn assembly (<c>Microsoft.CodeAnalysis.*</c>) is delegated back to the
    /// host's default context. That keeps a single <see cref="IIncrementalGenerator"/> type across
    /// host and generators, so the reflected generator instance can be cast to the host's interface
    /// and handed to <c>CSharpGeneratorDriver</c>. This mirrors how Roslyn itself loads analyzers.
    /// </summary>
    public sealed class GeneratorLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, string> _strongInjectAssemblies;

        public GeneratorLoadContext(GeneratorVersion version)
            : base($"StrongInject-{version}", isCollectible: true)
        {
            // Map the two StrongInject assemblies this build needs, by simple name -> path.
            _strongInjectAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["StrongInject.Generator.Roslyn40"] = GeneratorPaths.Roslyn40Dll(version),
                ["StrongInject.Generator"] = GeneratorPaths.GeneratorDll(version),
            };
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (name != null && _strongInjectAssemblies.TryGetValue(name, out var path))
            {
                return LoadFromAssemblyPath(path);
            }

            // Everything else (Microsoft.CodeAnalysis.*, System.*, netstandard, ...) resolves
            // from the host's default context so types unify with the benchmark process.
            return null;
        }

        /// <summary>Loads the generator entry point and adapts it to <see cref="ISourceGenerator"/>.</summary>
        public ISourceGenerator CreateGenerator(GeneratorVersion version)
        {
            var asm = LoadFromAssemblyPath(GeneratorPaths.Roslyn40Dll(version));
            var type = asm.GetType("StrongInject.Generator.IncrementalGenerator", throwOnError: true);
            var instance = Activator.CreateInstance(type, nonPublic: true);
            return ((IIncrementalGenerator)instance).AsSourceGenerator();
        }
    }
}
