using Serilog;
using Serilog.Events;

namespace Sentry.Samples.AspNetCore.Serilog;

public class Program
{
    public static void Main(string[] args) => BuildWebApp(args).Run();

    public static WebApplication BuildWebApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((_, c) =>
            c.Enrich.FromLogContext()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                // Add Sentry integration with Serilog
                // Two levels are used to configure it.
                // One sets which log level is minimally required to keep a log message as breadcrumbs
                // The other sets the minimum level for messages to be sent out as events to Sentry
                .WriteTo.Sentry(s =>
                {
                    s.MinimumBreadcrumbLevel = LogEventLevel.Debug;
                    s.MinimumEventLevel = LogEventLevel.Error;
                }));

        // Add Sentry integration
        // It can be defined via configuration (including `appsettings.json`)
        // or coded explicitly, via parameter like:
        // .UseSentry("dsn") or .UseSentry(o => o.Dsn = ""; o.Release = "1.0"; ...)
#if !SENTRY_DSN_DEFINED_IN_ENV
        builder.WebHost.UseSentry(SamplesShared.Dsn);
#else
        builder.WebHost.UseSentry(EnvironmentVariables.Dsn);
#endif

        // The App:
        var webApplication = builder.Build();
        Configure(webApplication);

        return webApplication;
    }

    private static void Configure(IApplicationBuilder app)
    {
        // An example ASP.NET Core middleware that throws an
        // exception when serving a request to path: /throw
        app.Use(async (context, next) =>
        {
            // See MinimumBreadcrumbLevel set at the Serilog configuration above
            Log.Logger.Debug("Static Serilog logger debug log stored as breadcrumbs.");

            var log = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger<Program>();

            log.LogInformation("Handling some request...");

            // Sends an event which includes the info and debug messages above
            Log.Logger.Error("Logging using static Serilog directly also goes to Sentry.");

            if (context.Request.Path == "/greetings")
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Hello, World!");
            }
            else if (context.Request.Path == "/throw")
            {
                var hub = context.RequestServices.GetRequiredService<IHub>();
                hub.ConfigureScope(s =>
                {
                    // More data can be added to the scope like this:
                    s.SetTag("Sample", "ASP.NET Core"); // indexed by Sentry
                    s.SetExtra("Extra!", "Some extra information");
                });

                // Logging through the ASP.NET Core `ILogger` while using Serilog
                log.LogInformation("Logging info...");
                log.LogWarning("Logging some warning!");

                // The following exception will be captured by the SDK and the event
                // will include the Log messages and any custom scope modifications
                // as exemplified above.
                throw new Exception("An exception thrown from the ASP.NET Core pipeline");
            }
            else
            {
                await next();
            }
        });
    }
}
