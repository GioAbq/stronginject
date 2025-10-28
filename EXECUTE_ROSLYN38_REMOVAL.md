# Execute: Remove Roslyn 3.8 Support

## Quick Checklist

- [ ] Remove `StrongInject.Generator.Roslyn38/` directory
- [ ] Remove from `StrongInject.sln`
- [ ] Update `StrongInject/StrongInject.csproj` (analyzer packaging)
- [ ] Update `StrongInject.Tests.Unit/TestBase.cs` (remove comparison)
- [ ] Update `StrongInject.Tests.Unit/*.csproj` (remove project reference)
- [ ] Run tests: `dotnet test` → Expect 100% pass rate
- [ ] Update README.md with minimum requirements
- [ ] Version bump to 2.0.0

## Commands to Execute

```bash
# 1. Remove Roslyn38 project
rm -rf StrongInject.Generator.Roslyn38

# 2. Update solution (remove project)
dotnet sln StrongInject.sln remove StrongInject.Generator.Roslyn38/StrongInject.Generator.Roslyn38.csproj

# 3. Remove test references
# (Manual edit: StrongInject.Tests.Unit/StrongInject.Tests.Unit.csproj)

# 4. Test everything
dotnet clean
dotnet build
dotnet test

# Expected: 312/312 tests pass (100%)
```

## Critical Files to Edit

### 1. StrongInject/StrongInject.csproj
Remove the Roslyn38 analyzer from packaging.

### 2. StrongInject.Tests.Unit/TestBase.cs
Already modified to run only IncrementalGenerator.

### 3. StrongInject.Tests.Unit/StrongInject.Tests.Unit.csproj  
Remove:
```xml
<ProjectReference Include="..\StrongInject.Generator.Roslyn38\StrongInject.Generator.Roslyn38.csproj" />
```

Keep:
```xml
<ProjectReference Include="..\StrongInject.Generator.Roslyn40\StrongInject.Generator.Roslyn40.csproj" />
```

## Post-Removal Verification

1. ✅ Build succeeds: `dotnet build StrongInject.sln`
2. ✅ All tests pass: `dotnet test` (expect 100%)
3. ✅ Package builds: `dotnet pack StrongInject/StrongInject.csproj`
4. ✅ Sample projects work with new package

## Migration Message for Users

```
StrongInject 2.0 Breaking Change

StrongInject 2.0 requires:
- Visual Studio 2022 or later
- .NET 6 SDK or later (recommend .NET 8 LTS)

If you're using .NET 5 or Visual Studio 2019, please:
- Stay on StrongInject 1.x, OR
- Upgrade to a supported platform (.NET 8 LTS recommended)

.NET 5 reached end-of-life in May 2022.
.NET 6 reached end-of-life in November 2024.
```

