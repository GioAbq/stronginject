using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using StrongInject;

namespace GenericInterfaceFactoryStrongInjectSample;

public class Program
{
    public static string DefaultConsoleOutputTemplate { get; set; } =
        "[{Timestamp:HH:mm:ss}|{Level:u3}] <s:{SourceContext}>{NewLine}   {Message:lj}  {Exception}{NewLine}";

    private Container _container;

    private static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: DefaultConsoleOutputTemplate)
            .CreateLogger();

        var program = new Program();
        program.Run();
    }

    private void Run()
    {
        _container = new();
        
        // Now we can resolve LoggerService which depends on ILogger<LoggerService>
        // No circular dependency error!
        using var owned = _container.Resolve();
        var service = owned.Value;
        service.Log("Hello from StrongInject!");
        
        Console.WriteLine("LoggerService successfully resolved with ILogger<LoggerService> dependency!");
    }
}