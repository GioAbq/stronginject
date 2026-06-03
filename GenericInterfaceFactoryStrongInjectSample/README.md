# GenericInterfaceFactoryStrongInjectSample

Demonstrates the StrongInject 2.0 `[FactoryOf]` feature: a factory method for an **open generic**
(`ILogger<>`) can now return the **constructed generic type** directly.

## What it shows

`Container.CreateLogger<T>` resolves `ILogger<T>` from a Serilog `SerilogLoggerFactory`:

```csharp
[FactoryOf(typeof(ILogger<>))]
public static ILogger<T> CreateLogger<T>(SerilogLoggerFactory loggerFactory)
    => loggerFactory.CreateLogger<T>();
```

Before 2.0, `[FactoryOf]` on an open generic had to return the bare type parameter `T`, forcing a
reflection-based workaround. 2.0 lets the method return `ILogger<T>` directly, so `LoggerService`
(which depends on `ILogger<LoggerService>`) resolves with no reflection and no circular-dependency error.

## Run

This sample references the in-repo StrongInject 2.0 generator directly (2.0 is not yet on NuGet), so it
builds against the generator in this repository:

```
dotnet run --project GenericInterfaceFactoryStrongInjectSample/GenericInterfaceFactoryStrongInjectSample
```

Standalone sample: it has its own `.sln` and is not part of `StrongInject.sln` or CI.
