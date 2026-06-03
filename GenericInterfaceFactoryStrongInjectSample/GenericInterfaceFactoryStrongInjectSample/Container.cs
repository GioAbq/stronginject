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

    // StrongInject 2.0: [FactoryOf] on an open generic can return the constructed generic type directly,
    // so ILogger<T> is resolved with no runtime reflection (pre-2.0 the method had to return the bare T).
    [FactoryOf(typeof(ILogger<>))]
    public static ILogger<T> CreateLogger<T>(SerilogLoggerFactory loggerFactory)
    {
        return loggerFactory.CreateLogger<T>();
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