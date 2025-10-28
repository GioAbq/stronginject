using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace StrongInject.Generator
{
    /// <summary>
    /// Cacheable data structures for incremental source generation.
    /// These types must NOT contain ISymbol, SyntaxNode, Compilation, or SemanticModel references.
    /// </summary>

    /// <summary>
    /// Represents a container or module that needs processing.
    /// This is cacheable because it only contains primitive types and Location.
    /// </summary>
    internal readonly record struct ContainerOrModuleInfo(
        string FullyQualifiedMetadataName,
        bool IsContainer,
        Location Location,
        EquatableArray<string> Interfaces)
    {
        public bool Equals(ContainerOrModuleInfo other)
        {
            return FullyQualifiedMetadataName == other.FullyQualifiedMetadataName
                && IsContainer == other.IsContainer
                && Interfaces.Equals(other.Interfaces);
            // Note: We don't compare Location for equality as it's not part of semantic identity
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + FullyQualifiedMetadataName.GetHashCode();
                hash = hash * 23 + IsContainer.GetHashCode();
                hash = hash * 23 + Interfaces.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// Equatable wrapper for ImmutableArray to enable proper caching.
    /// ImmutableArray{T} doesn't implement IEquatable properly for incremental generators.
    /// </summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
        where T : IEquatable<T>
    {
        public readonly ImmutableArray<T> Array;

        public EquatableArray(ImmutableArray<T> array)
        {
            Array = array;
        }

        public bool Equals(EquatableArray<T> other)
        {
            return Array.SequenceEqual(other.Array);
        }

        public override bool Equals(object? obj)
        {
            return obj is EquatableArray<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (var item in Array)
                {
                    hash = hash * 23 + (item?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
        {
            return !left.Equals(right);
        }
    }
}

