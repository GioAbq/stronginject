# Migration Plan: Roslyn 4.0 Only (Remove Roslyn 3.8)

## Decision: Drop Roslyn 3.8 Support

### Rationale
- .NET 5 SDK (requires Roslyn 3.8) is EOL since May 2022
- .NET 6 SDK (supports Roslyn 4.0+) is EOL since November 2024
- Current supported versions (.NET 8, 9, 10) all use Roslyn 4.0+
- Visual Studio 2022 (with Roslyn 4.0+) is the current standard

### Impact
Users on unsupported platforms will need to:
- Upgrade to Visual Studio 2022 (free Community edition available)
- Upgrade to .NET 6 SDK minimum (preferably .NET 8 LTS)

This is a **reasonable requirement** for a 2025 release.

## Migration Steps

### Step 1: Remove Roslyn38 Project
```bash
# Remove project
rm -rf StrongInject.Generator.Roslyn38

# Update solution file
# Remove StrongInject.Generator.Roslyn38 reference
```

### Step 2: Update Main Package
**File**: `StrongInject/StrongInject.csproj`

Remove Roslyn38 analyzer reference, keep only Roslyn40:
```xml
<ItemGroup>
  <!-- OLD: Both generators -->
  <None Include="..\StrongInject.Generator.Roslyn38\bin\$(Configuration)\netstandard2.0\StrongInject.Generator.Roslyn38.dll" Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  <None Include="..\StrongInject.Generator.Roslyn40\bin\$(Configuration)\netstandard2.0\StrongInject.Generator.Roslyn40.dll" Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  
  <!-- NEW: Only Roslyn40 -->
  <None Include="..\StrongInject.Generator.Roslyn40\bin\$(Configuration)\netstandard2.0\StrongInject.Generator.Roslyn40.dll" Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
</ItemGroup>
```

### Step 3: Update Tests
**File**: `StrongInject.Tests.Unit/TestBase.cs`

Remove old generator comparison:
```csharp
// OLD: Compare both generators
protected Compilation RunGenerator(Compilation compilation, out ImmutableArray<Diagnostic> diagnostics, out ImmutableArray<string> generatedFiles)
{
    CreateDriver(compilation, new SourceGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out diagnostics);
    CreateDriver(compilation, new IncrementalGenerator().AsSourceGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var incrementalCompilation, out var incrementalDiagnostics);
    // ... comparison logic ...
}

// NEW: Only incremental generator
protected Compilation RunGenerator(Compilation compilation, out ImmutableArray<Diagnostic> diagnostics, out ImmutableArray<string> generatedFiles)
{
    CreateDriver(compilation, new IncrementalGenerator().AsSourceGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out diagnostics);
    var generatedTrees = updatedCompilation.SyntaxTrees.Where(x => !compilation.SyntaxTrees.Any(y => y.Equals(x))).ToImmutableArray();
    generatedFiles = generatedTrees.Select(x => x.GetText().ToString()).ToImmutableArray();
    return updatedCompilation;
}
```

### Step 4: Update Project References
Remove `StrongInject.Generator.Roslyn38` from:
- `StrongInject.Tests.Unit/StrongInject.Tests.Unit.csproj`
- `StrongInject.sln`
- Any other project that references it

### Step 5: Update Documentation
**Files to update**:
- `README.md` - Add minimum requirements
- `CHANGELOG.md` - Breaking change notice
- NuGet package description

**Example**:
```markdown
## Minimum Requirements (v2.0+)
- Visual Studio 2022 or later
- .NET 6 SDK or later (preferably .NET 8 LTS)
- Rider 2021.3 or later

For older versions (.NET 5, VS 2019), please use StrongInject v1.x
```

## Expected Outcomes

### Benefits
1. ✅ **100% test pass rate** - No more generator comparison failures
2. ✅ **Better performance** - Only incremental generator
3. ✅ **Simpler codebase** - One generator to maintain
4. ✅ **Faster CI/CD** - No need to build/test Roslyn38
5. ✅ **Cleaner NuGet package** - Smaller size

### Breaking Changes
- Users on .NET 5 / VS 2019 must upgrade
- This is acceptable as both are EOL

## Version Strategy

Recommend releasing as **StrongInject 2.0** (major version bump):
- v1.x = Last version supporting Roslyn 3.8 / .NET 5
- v2.x = Roslyn 4.0+ only with incremental generators

This clearly signals the breaking change to users.

