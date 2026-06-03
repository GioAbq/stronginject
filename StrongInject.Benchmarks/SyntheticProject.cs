using System.Collections.Generic;
using System.Text;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// Builds a representative StrongInject project as a set of source files (one syntax tree each).
    /// Deliberately uses only plain <c>[Register]</c> / <c>[RegisterModule]</c> / sync <c>IContainer&lt;T&gt;</c>
    /// so the SAME source is valid and generates identically under BOTH 1.4.4 and 2.0
    /// (open-generic factories are avoided - their validation diverges between the versions).
    ///
    /// Shape:
    ///  - Shared.cs:       a dependency chain S0..S{L-1} (S{k} ctor-depends on S{k-1}).
    ///  - Modules.cs:      <see cref="ModuleCount"/> module classes, each [Register]-ing a slice of the chain.
    ///  - Container_{c}.cs: one tree per container; imports every module and resolves its own Leaf_{c}
    ///                      (Leaf depends on the top of the chain -> full-chain resolution = real work).
    ///  - Unrelated.cs:    a plain class with no StrongInject attributes (the file edited in the
    ///                      "unrelated edit" scenario).
    ///
    /// Pipeline items (= RegisterSourceOutput invocations) = ModuleCount + ContainerCount.
    /// </summary>
    public sealed class SyntheticProject
    {
        public const int ModuleCount = 8;
        public const int ServicesPerModule = 4;
        public const int ContainerCount = 30;

        /// <summary>Total length of the shared dependency chain.</summary>
        public int SharedChainLength => ModuleCount * ServicesPerModule;

        public const string SharedPath = "Shared.cs";
        public const string ModulesPath = "Modules.cs";
        public const string UnrelatedPath = "Unrelated.cs";

        public static string ContainerPath(int index) => $"Container_{index}.cs";

        public IReadOnlyList<(string Path, string Source)> Files()
        {
            var files = new List<(string, string)>
            {
                (SharedPath, SharedSource()),
                (ModulesPath, ModulesSource()),
            };
            for (var c = 0; c < ContainerCount; c++)
                files.Add((ContainerPath(c), ContainerSource(c, extraMembers: 0)));
            files.Add((UnrelatedPath, UnrelatedSource(salt: 0)));
            return files;
        }

        private string SharedSource()
        {
            var sb = new StringBuilder();
            for (var k = 0; k < SharedChainLength; k++)
            {
                if (k == 0)
                    sb.AppendLine($"public class S{k} {{ }}");
                else
                    sb.AppendLine($"public class S{k} {{ public S{k}(S{k - 1} dep) {{ }} }}");
            }
            return sb.ToString();
        }

        private string ModulesSource()
        {
            var sb = new StringBuilder();
            sb.AppendLine("using StrongInject;");
            sb.AppendLine();
            for (var j = 0; j < ModuleCount; j++)
            {
                for (var s = 0; s < ServicesPerModule; s++)
                {
                    var idx = j * ServicesPerModule + s;
                    sb.AppendLine($"[Register(typeof(S{idx}))]");
                }
                sb.AppendLine($"public class Module_{j} {{ }}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Source for one container. <paramref name="extraMembers"/> appends trivial methods to mutate
        /// the syntax tree (used by the "container edit" scenario) without changing any registration.
        /// </summary>
        public string ContainerSource(int index, int extraMembers)
        {
            var top = SharedChainLength - 1;
            var sb = new StringBuilder();
            sb.AppendLine("using StrongInject;");
            sb.AppendLine();
            for (var j = 0; j < ModuleCount; j++)
                sb.AppendLine($"[RegisterModule(typeof(Module_{j}))]");
            sb.AppendLine($"[Register(typeof(Leaf_{index}))]");
            sb.AppendLine($"public partial class Container_{index} : IContainer<Leaf_{index}>");
            sb.AppendLine("{");
            for (var m = 0; m < extraMembers; m++)
                sb.AppendLine($"    public int Ping_{m}() => {m};");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"public class Leaf_{index} {{ public Leaf_{index}(S{top} top) {{ }} }}");
            return sb.ToString();
        }

        /// <summary>A plain (non-StrongInject) file. <paramref name="salt"/> changes the literal to mutate the tree.</summary>
        public string UnrelatedSource(int salt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("namespace Unrelated_NS");
            sb.AppendLine("{");
            sb.AppendLine("    public class Unrelated");
            sb.AppendLine("    {");
            sb.AppendLine($"        public int Value => {salt};");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
