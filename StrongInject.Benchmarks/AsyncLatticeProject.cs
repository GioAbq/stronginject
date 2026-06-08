using System.Collections.Generic;
using System.Text;

namespace StrongInject.Benchmarks
{
    /// <summary>
    /// A parameterized ASYNC dependency lattice, built to exercise the async-only ordering pass
    /// <c>LoweringVisitor.Order</c> / <c>FindLongestPath</c> (StrongInject.Generator/Visitors/LoweringVisitor.cs),
    /// which runs an UN-MEMOIZED depth-first search over the operation dependency graph.
    ///
    /// The version-neutral <see cref="SyntheticProject"/> is 100% sync (<c>IContainer&lt;T&gt;</c>), so that
    /// async-only code path is never measured by it. Here every node is produced by an async
    /// <c>[Factory]</c> returning <c>ValueTask&lt;T&gt;</c> and each node at layer L depends on ALL
    /// <see cref="Width"/> nodes of layer L+1, so the number of distinct root-to-leaf paths is
    /// <c>Width^Depth</c> - the worst case for an un-memoized longest-path search. Sweeping
    /// <see cref="Depth"/> turns the suspected cliff into a measured curve.
    ///
    /// Shape:
    ///  - layer 0: a single root node, resolved by the container (<c>IAsyncContainer&lt;N0_0&gt;</c>);
    ///  - layers 1..Depth: <see cref="Width"/> nodes each;
    ///  - node N{L}_i (for L &lt; Depth) is created by an async factory taking all Width nodes of layer L+1;
    ///  - layer Depth nodes are async leaves (no dependencies).
    /// </summary>
    public sealed class AsyncLatticeProject
    {
        public int Width { get; }
        public int Depth { get; }

        public AsyncLatticeProject(int width, int depth)
        {
            Width = width;
            Depth = depth;
        }

        /// <summary>Total async factory nodes = 1 root + Width*Depth.</summary>
        public int NodeCount => 1 + Width * Depth;

        public const string NodesPath = "AsyncNodes.cs";
        public const string ModulePath = "AsyncModule.cs";
        public const string ContainerPath = "AsyncContainer.cs";

        public IReadOnlyList<(string Path, string Source)> Files() => new[]
        {
            (NodesPath, NodesSource()),
            (ModulePath, ModuleSource()),
            (ContainerPath, ContainerSource()),
        };

        private static string Node(int layer, int index) => $"N{layer}_{index}";

        // Layer 0 holds the single root; every deeper layer holds Width nodes.
        private int CountAt(int layer) => layer == 0 ? 1 : Width;

        private string NodesSource()
        {
            var sb = new StringBuilder();
            for (var layer = 0; layer <= Depth; layer++)
                for (var i = 0; i < CountAt(layer); i++)
                    sb.AppendLine($"public sealed class {Node(layer, i)} {{ }}");
            return sb.ToString();
        }

        private string ModuleSource()
        {
            var sb = new StringBuilder();
            sb.AppendLine("using StrongInject;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine("public class AsyncLatticeModule");
            sb.AppendLine("{");
            for (var layer = 0; layer <= Depth; layer++)
            {
                for (var i = 0; i < CountAt(layer); i++)
                {
                    var node = Node(layer, i);
                    var parameters = new List<string>();
                    if (layer < Depth)
                        for (var d = 0; d < CountAt(layer + 1); d++)
                            parameters.Add($"{Node(layer + 1, d)} d{d}");

                    sb.AppendLine("    [Factory]");
                    sb.AppendLine($"    public static ValueTask<{node}> Create_{node}({string.Join(", ", parameters)}) => default;");
                }
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string ContainerSource()
        {
            var sb = new StringBuilder();
            sb.AppendLine("using StrongInject;");
            sb.AppendLine();
            sb.AppendLine("[RegisterModule(typeof(AsyncLatticeModule))]");
            sb.AppendLine($"public partial class AsyncLatticeContainer : IAsyncContainer<{Node(0, 0)}>");
            sb.AppendLine("{");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
