using EchoRankedServerBot.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

TaskScheduler.UnobservedTaskException += (_, args) =>
{
    Console.Error.WriteLine("An unobserved task exception occurred and was logged.");
    Console.Error.WriteLine(args.Exception);
    args.SetObserved();
};

AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    Console.Error.WriteLine("An unhandled exception is terminating the bot.");
    Console.Error.WriteLine(args.ExceptionObject);
};

try
{
    var builder = Host.CreateApplicationBuilder(args);

#if DEBUG
    builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
#endif

    EchoRankedServerBot.Extensions.ConfigurationExtensions.Initialize(builder.Configuration);

    builder.Services.AddEchoRankedBot(builder.Configuration);

    var host = builder.Build();

    try
    {
        await host.RunAsync();
        return 0;
    }
    catch (Exception ex)
    {
        var logger = host.Services.GetService<ILogger<Program>>();
        logger?.LogCritical(ex, "The bot failed to start: {Message}", ex.Message);

        Console.Error.WriteLine($"The bot failed to start: {ex.Message}");
        Console.Error.WriteLine(ex);
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"The bot failed to start: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 1;
}
