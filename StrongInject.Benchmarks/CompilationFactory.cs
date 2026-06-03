using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// Builds the <see cref="CSharpCompilation"/> for the synthetic project. The metadata-reference
    /// set mirrors <c>StrongInject.Tests.Unit/TestBase.cs</c> (proven to compile StrongInject code),
    /// swapping in the version-specific <c>StrongInject.dll</c>.
    /// </summary>
    public static class CompilationFactory
    {
        public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

        public static SyntaxTree ParseFile(string path, string source)
            => CSharpSyntaxTree.ParseText(source, ParseOptions, path: path);

        public static CSharpCompilation Create(GeneratorVersion version, SyntheticProject project, out ImmutableArray<SyntaxTree> trees)
        {
            trees = project.Files()
                .Select(f => ParseFile(f.Path, f.Source))
                .ToImmutableArray();

            return CSharpCompilation.Create(
                "SyntheticBenchmark",
                trees,
                BuildReferences(version),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        }

        private static IReadOnlyList<MetadataReference> BuildReferences(GeneratorVersion version)
        {
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

            var paths = new[]
            {
                typeof(Binder).Assembly.Location,                                  // System.Reflection.Binder -> CoreLib
                typeof(object).Assembly.Location,
                typeof(Attribute).Assembly.Location,
                typeof(ValueTask).Assembly.Location,
                typeof(IAsyncEnumerable<>).Assembly.Location,
                typeof(ConcurrentBag<>).Assembly.Location,
                typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location,
                Path.Combine(coreDir, "netstandard.dll"),
                Path.Combine(coreDir, "System.Runtime.dll"),
                GeneratorPaths.StrongInjectDll(version),
            };

            return paths
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToImmutableArray();
        }
    }
}
