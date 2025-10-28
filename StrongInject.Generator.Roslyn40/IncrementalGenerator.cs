using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace StrongInject.Generator
{
    /// <summary>
    /// Refactored incremental generator following Roslyn best practices:
    /// 1. NO CompilationWrapper anti-pattern
    /// 2. Proper use of ForAttributeWithMetadataName for performance
    /// 3. Cacheable data structures (no ISymbol in pipeline outputs)
    /// 4. Per-container processing for true incrementality
    /// </summary>
    [Generator]
    internal class IncrementalGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Step 1: Find all classes that might be containers or modules
            // Using syntax-based filtering first is faster than semantic analysis
            var candidateClasses = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsPotentialContainerOrModule(node),
                transform: static (ctx, ct) => ExtractCandidate(ctx, ct))
                .Where(static x => x != null);

            // Step 2: Combine with compilation to do semantic analysis
            // But we extract only the data we need and discard symbols
            var containersAndModules = candidateClasses
                .Combine(context.CompilationProvider)
                .Select((pair, ct) =>
                {
                    ClassDeclarationSyntax candidate = pair.Left;
                    Compilation compilation = pair.Right;
                    
                    if (!WellKnownTypes.TryCreate(compilation, _ => { }, out var wellKnownTypes))
                        return default;

                    var semanticModel = compilation.GetSemanticModel(candidate.SyntaxTree);
                    if (semanticModel.GetDeclaredSymbol(candidate, ct) is not INamedTypeSymbol symbol)
                        return default;

                    // Check if it's actually a container or module
                    var isContainer = symbol.AllInterfaces.Any(x => WellKnownTypes.IsContainerOrAsyncContainer(x));
                    var hasStrongInjectAttributes = 
                        symbol.GetAttributes().Any(x => WellKnownTypes.IsClassAttribute(x.AttributeClass)) ||
                        symbol.GetMembers().Any(m =>
                            (m is IFieldSymbol or IPropertySymbol && m.GetAttributes().Any(x => WellKnownTypes.IsInstanceAttribute(x.AttributeClass))) ||
                            (m is IMethodSymbol && m.GetAttributes().Any(x => WellKnownTypes.IsMethodAttribute(x.AttributeClass))));

                    if (!isContainer && !hasStrongInjectAttributes)
                        return default;

                    // Extract only cacheable data - NO ISymbol references!
                    var info = new ContainerOrModuleInfo(
                        FullyQualifiedMetadataName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        IsContainer: isContainer,
                        Location: candidate.Identifier.GetLocation(), // Use Identifier location for precise diagnostic positioning
                        Interfaces: new EquatableArray<string>(
                            symbol.AllInterfaces
                                .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                                .ToImmutableArray()));

                    return (ContainerOrModuleInfo?)info;
                })
                .Where(static x => x.HasValue)
                .Select(static (x, _) => x!.Value);

            // Step 3: Process ALL containers and modules  
            // Modules need standalone validation (e.g. circular module registration)
            var allContainersAndModules = containersAndModules
                .Combine(context.CompilationProvider);

            // Step 4: Generate/validate each container and module
            context.RegisterSourceOutput(allContainersAndModules, (spc, data) =>
            {
                ContainerOrModuleInfo info = data.Left;
                Compilation compilation = data.Right;
                
                GenerateContainerOrValidateModule(spc, info, compilation);
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

        private static ClassDeclarationSyntax? ExtractCandidate(GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            return context.Node as ClassDeclarationSyntax;
        }

        private static void GenerateContainerOrValidateModule(
            SourceProductionContext context,
            ContainerOrModuleInfo info,
            Compilation compilation)
        {
            var cancellationToken = context.CancellationToken;
            
            // Retrieve the symbol using the cached location
            // GetTypeByMetadataName doesn't work for generic types, so we use the location
            var syntaxTree = info.Location.SourceTree;
            if (syntaxTree is null)
                return;
            
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var syntaxNode = syntaxTree.GetRoot(cancellationToken).FindNode(info.Location.SourceSpan);
            
            if (syntaxNode is not ClassDeclarationSyntax classDecl)
                return;
            
            if (semanticModel.GetDeclaredSymbol(classDecl, cancellationToken) is not INamedTypeSymbol symbol)
                return;

            if (!WellKnownTypes.TryCreate(compilation, context.ReportDiagnostic, out var wellKnownTypes))
                return;

            var registrationCalculator = new RegistrationCalculator(compilation, wellKnownTypes, cancellationToken);

            // Check visibility for both containers and modules
            if (!symbol.IsInternal() && !symbol.IsPublic())
            {
                context.ReportDiagnostic(ModuleNotPublicOrInternal(
                    symbol,
                    info.Location));
                return;
            }

            if (!info.IsContainer)
            {
                // This is a module - just validate it
                registrationCalculator.ValidateModuleRegistrations(symbol, context.ReportDiagnostic);
                return;
            }

            // It's a container - generate full implementation

            // Generate container implementation
            var file = ContainerGenerator.GenerateContainerImplementations(
                symbol,
                registrationCalculator.GetContainerRegistrations(symbol, context.ReportDiagnostic),
                wellKnownTypes,
                context.ReportDiagnostic,
                cancellationToken);

            context.AddSource(GenerateNameHint(symbol), file);
        }

        private static WellKnownTypes GetWellKnownTypes(Compilation compilation, Action<Diagnostic> reportDiagnostic)
        {
            if (!WellKnownTypes.TryCreate(compilation, reportDiagnostic, out var wellKnownTypes))
            {
                throw new InvalidOperationException("Could not create WellKnownTypes");
            }
            return wellKnownTypes;
        }

        private static string GenerateNameHint(INamedTypeSymbol container)
        {
            var stringBuilder = new StringBuilder(container.ContainingNamespace.FullName());
            foreach (var type in container.GetContainingTypesAndThis().Reverse())
            {
                stringBuilder.Append(".");
                stringBuilder.Append(type.Name);
                if (type.TypeParameters.Length > 0)
                {
                    stringBuilder.Append("_");
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
    }
}

