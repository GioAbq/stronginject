# Changelog

All notable changes to StrongInject will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - Unreleased

### 🚀 Major Changes

#### Migrated to Incremental Source Generators (Roslyn 4.0+)

StrongInject 2.0 now uses the new **Incremental Source Generators API** introduced in Roslyn 4.0. This brings significant performance improvements:

- **Faster IDE Performance**: Incremental compilation means source generation only runs on changed files, not the entire solution on every edit.
- **Better Caching**: Intermediate results are cached efficiently, reducing redundant computation.
- **Lower Memory Usage**: More efficient memory management in Visual Studio 2022 and later.
- **True Incrementality**: Container-by-container regeneration instead of reprocessing all syntax trees.

#### ⚠️ Breaking Changes

**Minimum Requirements:**
- **Visual Studio 2022** or later (Visual Studio 2019 is no longer supported)
- **.NET 6 SDK** or later (recommend .NET 8 or .NET 10)

**Why the Breaking Change?**  
The Incremental Generators API (`IIncrementalGenerator`) is only available in Roslyn 4.0+, which ships with:
- Visual Studio 2022 (17.0+)
- .NET 6 SDK and later

Visual Studio 2019 and .NET 5 SDK use Roslyn 3.x and cannot run incremental generators.

**Migration Path:**
- **For new projects or projects on VS 2022/.NET 6+**: Upgrade to StrongInject 2.0 for better performance.
- **For legacy projects on VS 2019/.NET 5**: Continue using StrongInject 1.x by pinning to `Version="1.*"` in your `.csproj`:
  ```xml
  <PackageReference Include="StrongInject" Version="1.*" />
  ```

### 🔧 Technical Details

- Removed `StrongInject.Generator.Roslyn38` project
- `StrongInject` NuGet package now ships only the Roslyn 4.0+ analyzer (`StrongInject.Generator.Roslyn40.dll`)
- All tests (unit, integration, extensions) updated and passing (100% pass rate)
- CI updated to .NET 6 + .NET 8 multi-version support

### 📦 Package Changes

- **Analyzer Location**: `analyzers/dotnet/cs/StrongInject.Generator.Roslyn40.dll`
- **API Surface**: Source-compatible with 1.x (`netstandard2.0` surface maintained); 2.0 only adds non-breaking hardening - standard `StrongInjectException` constructors, argument null-validation in the container extension methods, and `sealed` on the obsolete attributes
- **Backwards Compatibility**: Code using StrongInject 1.x will compile without changes in 2.0, but **build toolchain** must support Roslyn 4.0+

### 🐛 Bug Fixes

- Fixed diagnostic location precision (now points to class identifier instead of entire declaration)
- Fixed module validation order to ensure all diagnostics are reported
- Fixed generic type symbol resolution in incremental generator pipeline

### 📚 Documentation

- Updated README.md with new requirements and migration guidance
- Added CHANGELOG.md for version tracking
- Maintained compatibility guide for VS 2019/.NET 5 users

---

## [1.x] - Legacy Branch

StrongInject 1.x is frozen at the last published 1.4.x package on NuGet. There is no `release/1.x` maintenance branch, and no further 1.x updates (including bug fixes) are planned.

**Minimum Requirements for 1.x:**
- Visual Studio 16.8 or later
- .NET 5.0.102 SDK or later

**Note**: No new features will be added to 1.x. All new development is on 2.0+.

---

## Version History

- **2.0.0** - Incremental Generators (Roslyn 4.0+), VS 2022/.NET 6+ only
- **1.x** - Legacy (Roslyn 3.8+), VS 2019/.NET 5+ support

For detailed commit history, see [GitHub Releases](https://github.com/YairHalberstadt/stronginject/releases).

