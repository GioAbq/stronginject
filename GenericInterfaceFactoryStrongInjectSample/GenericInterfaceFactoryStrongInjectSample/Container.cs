using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
using StrongInject;

namespace GenericInterfaceFactoryStrongInjectSample;

[Register(typeof(LoggerService))]
public partial class Container : IContainer<LoggerService>
{
    [Instance] public static ILoggerProvider[] LoggerProviders { get; set; } = Array.Empty<ILoggerProvider>();

    [Factory(Scope.SingleInstance)]
    public static SerilogLoggerFactory GetSerilogLoggerFactory(LoggerProviderCollection providerCollection)
    {
        return new(null, true, providerCollection);
    }

    // Current workaround: StrongInject requires FactoryOf with open generic to return the type parameter directly
    // We wrap it to return ILogger<T>
    [FactoryOf(typeof(ILogger<>))]
    public static T CreateLogger<T>(SerilogLoggerFactory loggerFactory) where T : class
    {
        // T will be ILogger<SomeType>, extract SomeType and create the logger
        var loggerType = typeof(T);
        if (!loggerType.IsGenericType || loggerType.GetGenericTypeDefinition() != typeof(ILogger<>))
        {
            throw new InvalidOperationException($"CreateLogger can only create ILogger<T>, not {typeof(T)}");
        }
        
        var targetType = loggerType.GetGenericArguments()[0];
        var method = typeof(SerilogLoggerFactory).GetMethod(nameof(SerilogLoggerFactory.CreateLogger))!
            .MakeGenericMethod(targetType);
        return (T)method.Invoke(loggerFactory, null)!;
    }

    [Factory(Scope.SingleInstance)]
    public static LoggerProviderCollection GetLoggerProviderCollection(ILoggerProvider[] loggerProviders)
    {
        if (loggerProviders == null)
            throw new ArgumentNullException(nameof(loggerProviders));
            
        var collection = new LoggerProviderCollection();
        foreach (var loggerProvider in loggerProviders)
            collection.AddProvider(loggerProvider);
        return collection;
    }
}