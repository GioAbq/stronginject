using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using System.Threading;

namespace StrongInject.Generator
{
    /// <summary>
    /// Incremental generator for StrongInject containers and modules.
    ///
    /// Design notes (why it looks the way it does):
    /// - The syntax provider carries the actual <see cref="ClassDeclarationSyntax"/> node through the
    ///   pipeline, so the container/module symbol is always resolved from the SAME compilation snapshot
    ///   the node belongs to. Re-deriving the symbol from a cached <see cref="Location"/> against a later
    ///   compilation is unsafe: an edited file's old tree is no longer part of the new compilation and
    ///   <c>GetSemanticModel</c> throws (CS8785).
    /// - <see cref="CompilationWrapper"/> wraps the compilation with an <c>Equals</c> that always returns
    ///   true, so a compilation change does NOT invalidate the final <c>Combine</c>. RegisterSourceOutput
    ///   then re-runs only when a tracked class's syntax actually changes - editing an unrelated file
    ///   regenerates nothing. The callback still observes the freshest compilation via the wrapper property.
    /// </summary>
    [Generator]
    internal class IncrementalGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Cheap syntax-level filtering first, then semantic confirmation. Returns the node itself so
            // the symbol can be resolved consistently from the compilation in the output stage.
            var candidates = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsPotentialContainerOrModule(node),
                transform: static (ctx, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, cancellationToken) is not INamedTypeSymbol type)
                    {
                        return default;
                    }

                    var isContainer = type.AllInterfaces.Any(x => WellKnownTypes.IsContainerOrAsyncContainer(x));

                    cancellationToken.ThrowIfCancellationRequested();
                    if (!isContainer
                        && !type.GetAttributes().Any(x => WellKnownTypes.IsClassAttribute(x.AttributeClass))
                        && !type.GetMembers().Any(x =>
                        {
                            if (x is IFieldSymbol or IPropertySymbol && x.GetAttributes().Any(a => WellKnownTypes.IsInstanceAttribute(a.AttributeClass)))
                            {
                                return true;
                            }

                            return x is IMethodSymbol && x.GetAttributes().Any(a => WellKnownTypes.IsMethodAttribute(a.AttributeClass));
                        }))
                    {
                        return default;
                    }

                    return (isContainer, node: (ClassDeclarationSyntax)ctx.Node);
                });

            var compilationWrapper = context.CompilationProvider.Select(static (x, _) => new CompilationWrapper(x));

            context.RegisterSourceOutput(candidates.Combine(compilationWrapper), static (context, pair) =>
            {
                var (isContainer, node) = pair.Left;
                if (node is null)
                {
                    return;
                }

                GenerateContainerOrValidateModule(context, isContainer, node, pair.Right.Compilation);
            });
        }

        private static bool IsPotentialContainerOrModule(SyntaxNode node)
        {
            if (node is not ClassDeclarationSyntax
                {
                    BaseList: var baseList,
                    AttributeLists: var attributes,
                    Members: var members,
                })
            {
                return false;
            }

            // Check if class implements IContainer/IAsyncContainer interface
            if (baseList is not null)
            {
                foreach (var type in baseList.Types)
                {
                    if (type.Type is NameSyntax name && WellKnownTypes.IsContainerCandidate(name))
                    {
                        return true;
                    }
                }
            }

            // Check for StrongInject attributes on the class
            foreach (var attributeList in attributes)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (WellKnownTypes.IsClassAttributeCandidate(attribute.Name))
                    {
                        return true;
                    }
                }
            }

            // Check for StrongInject attributes on members (fields, properties, methods)
            foreach (var member in members)
            {
                foreach (var attributeList in member.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        if (WellKnownTypes.IsMemberAttributeCandidate(attribute.Name))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void GenerateContainerOrValidateModule(
            SourceProductionContext context,
            bool isContainer,
            ClassDeclarationSyntax node,
            Compilation compilation)
        {
            var cancellationToken = context.CancellationToken;

            // Resolve the symbol from the node's own tree within THIS compilation. Because the node flows
            // from the syntax provider tracking this compilation, its tree is guaranteed to be part of it.
            var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(node, cancellationToken) is not INamedTypeSymbol symbol)
            {
                return;
            }

            if (!WellKnownTypes.TryCreate(compilation, context.ReportDiagnostic, out var wellKnownTypes))
            {
                return;
            }

            var location = node.Identifier.GetLocation();
            var registrationCalculator = new RegistrationCalculator(compilation, wellKnownTypes, cancellationToken);

            if (!isContainer)
            {
                // This is a module - validate registrations first, then check visibility
                registrationCalculator.ValidateModuleRegistrations(symbol, context.ReportDiagnostic);

                // Check visibility (report but don't early return to allow registration validation)
                if (!symbol.IsInternal() && !symbol.IsPublic())
                {
                    context.ReportDiagnostic(ModuleNotPublicOrInternal(symbol, location));
                }
                return;
            }

            // Check visibility for containers
            if (!symbol.IsInternal() && !symbol.IsPublic())
            {
                context.ReportDiagnostic(ModuleNotPublicOrInternal(symbol, location));
                return;
            }

            // It's a container - generate full implementation
            var file = ContainerGenerator.GenerateContainerImplementations(
                symbol,
                registrationCalculator.GetContainerRegistrations(symbol, context.ReportDiagnostic),
                wellKnownTypes,
                context.ReportDiagnostic,
                cancellationToken);

            context.AddSource(GenerateNameHint(symbol), file);
        }

        private static string GenerateNameHint(INamedTypeSymbol container)
        {
            var stringBuilder = new StringBuilder(container.ContainingNamespace.FullName());
            foreach (var type in container.GetContainingTypesAndThis().Reverse())
            {
                stringBuilder.Append('.');
                stringBuilder.Append(type.Name);
                if (type.TypeParameters.Length > 0)
                {
                    stringBuilder.Append('_');
                    stringBuilder.Append(type.TypeParameters.Length);
                }
            }

            stringBuilder.Append(".g.cs");
            return stringBuilder.ToString();
        }

        private static Diagnostic ModuleNotPublicOrInternal(ITypeSymbol module, Location location)
        {
            return Diagnostic.Create(
                new DiagnosticDescriptor(
                    "SI0401",
                    "Module must be public or internal.",
                    "Module '{0}' must be public or internal.",
                    "StrongInject",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location,
                module.ToDisplayString());
        }

        /// <summary>
        /// Wraps a <see cref="Compilation"/> so that incremental comparison never reports a change:
        /// <see cref="Equals(CompilationWrapper)"/> always returns true (while swapping in the newest
        /// compilation by version). This lets the final Combine stay cached across compilation changes,
        /// so source output re-runs only when the tracked class syntax changes.
        /// </summary>
        private class CompilationWrapper : IEquatable<CompilationWrapper>
        {
            // We need to lock both this and other for Equals, which is difficult to do without risking a deadlock.
            // Given how rarely Equals is likely to be called, just use a shared lock among all instances.
            private static readonly object _lock = new();
            public Compilation Compilation
            {
                get
                {
                    lock (_lock)
                    {
                        return _compilation;
                    }
                }
            }

            private long _version;
            private Compilation _compilation;
            private static long _nextVersion;

            public CompilationWrapper(Compilation compilation)
            {
                _compilation = compilation;
                _version = Interlocked.Increment(ref _nextVersion);
            }

            public bool Equals(CompilationWrapper? other)
            {
                if (other is null)
                    return false;
                lock (_lock)
                {
                    if (other._version > _version)
                    {
                        (_compilation, _version) = (other._compilation, other._version);
                    }
                    else
                    {
                        (other._compilation, other._version) = (_compilation, _version);
                    }
                }

                return true;
            }

            public override bool Equals(object? obj)
            {
                return Equals(obj as CompilationWrapper);
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
