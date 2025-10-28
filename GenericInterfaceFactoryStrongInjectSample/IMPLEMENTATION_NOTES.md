# ILogger<> Generic Resolution - Implementation Notes

## Problem

The original implementation had several issues:

1. **Incorrect Method Signature**: The `CreateLogger` method returned `T` instead of `ILogger<T>`
2. **Unnecessary Parameter**: Required `T loggerRequestingInstance` parameter
3. **Runtime Reflection**: Used expensive reflection to create loggers at runtime
4. **Generator Limitation**: StrongInject's current implementation requires `FactoryOf` methods with open generics to return the single type parameter directly

## Current Solution (Workaround)

The current implementation works around the generator limitation by:

```csharp
[FactoryOf(typeof(ILogger<>))]
public static T CreateLogger<T>(SerilogLoggerFactory loggerFactory) where T : class
{
    // T will be ILogger<SomeType>, so we extract SomeType and create the logger
    var targetType = typeof(T).GetGenericArguments()[0];
    var method = typeof(SerilogLoggerFactory).GetMethod(nameof(SerilogLoggerFactory.CreateLogger))!
        .MakeGenericMethod(targetType);
    return (T)method.Invoke(loggerFactory, null)!;
}
```

This allows StrongInject to successfully resolve `ILogger<LoggerService>` as a dependency of `LoggerService` without circular dependency errors.

## Ideal Solution (Requires Generator Changes)

The ideal API would be:

```csharp
[FactoryOf(typeof(ILogger<>))]
public static ILogger<T> CreateLogger<T>(SerilogLoggerFactory loggerFactory)
{
    return loggerFactory.CreateLogger<T>();
}
```

This requires changing the generator's validation in `RegistrationCalculator.CreateInstanceSourcesIfFactoryOfMethod` (lines 921-930) to allow:
- Returning a constructed generic type that uses the type parameter
- Example: `ILogger<T>` where `T` is the type parameter

## Testing

The sample now successfully:
1. Resolves `LoggerService` which depends on `ILogger<LoggerService>`
2. No circular dependency errors
3. Uses dependency injection properly

## Next Steps for Generator Improvement

1. Modify validation in `RegistrationCalculator.cs` line ~923-930 to allow constructed generic return types
2. Update `FactoryOfMethod` processing to handle this pattern
3. Add unit tests for this scenario
4. Update documentation with the improved pattern

