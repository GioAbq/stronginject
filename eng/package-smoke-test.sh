#!/usr/bin/env bash
#
# Package smoke test: install the packed StrongInject NuGet package into a
# throwaway consumer that declares a real container, then build it and assert
# code generation actually runs.
#
# This catches packaging regressions that the unit tests cannot see: TestBase
# runs the generator via project references, so it never exercises the packaged
# analyzer layout. A missing analyzer dependency assembly (e.g.
# StrongInject.Generator.dll) makes generation fail with FileNotFoundException
# at the consumer, even though every unit test stays green.
#
# Usage: eng/package-smoke-test.sh [feed-dir]   (default feed-dir: out)

set -euo pipefail

FEED_DIR="${1:-out}"
FEED_ABS="$(cd "$FEED_DIR" && pwd)"

# Discover the packed StrongInject version from the feed (ignore Extensions/symbols).
PKG="$(ls "$FEED_ABS"/StrongInject.*.nupkg 2>/dev/null | grep -vE 'Extensions|\.symbols\.' | head -n1 || true)"
if [[ -z "$PKG" ]]; then
  echo "SMOKE TEST FAILED: no StrongInject.*.nupkg found in '$FEED_ABS'"
  exit 1
fi
VERSION="$(basename "$PKG" | sed -E 's/^StrongInject\.(.*)\.nupkg$/\1/')"
TFM="${SMOKE_TFM:-net8.0}"
echo "Smoke testing StrongInject package version: $VERSION (consumer TFM: $TFM)"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT
cd "$WORKDIR"

dotnet new console -n Consumer -f "$TFM" >/dev/null

cat > Consumer/nuget.config <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="smoke-feed" value="$FEED_ABS" />
  </packageSources>
</configuration>
EOF

cat > Consumer/Program.cs <<'EOF'
using StrongInject;

[Register(typeof(Service))]
public partial class Container : IContainer<Service> { }

public class Service { }

public static class Program
{
    public static void Main()
    {
        using var container = new Container();
        var name = container.Run((service, _) => service.GetType().Name, 0);
        System.Console.WriteLine(name);
    }
}
EOF

cd Consumer
dotnet add package StrongInject --version "$VERSION" >/dev/null

LOG="$WORKDIR/build.log"
if ! dotnet build -c Release -p:TreatWarningsAsErrors=false 2>&1 | tee "$LOG"; then
  echo "SMOKE TEST FAILED: consumer build failed - generated container did not compile."
  exit 1
fi

# Belt and braces: a CS8785 generator failure can still leave a (broken) build
# green in edge cases. Treat any generator failure as a hard smoke-test failure.
if grep -q "CS8785" "$LOG"; then
  echo "SMOKE TEST FAILED: generator reported CS8785 (it could not produce source)."
  exit 1
fi

echo "SMOKE TEST PASSED: StrongInject $VERSION generates and compiles a container from the package."
