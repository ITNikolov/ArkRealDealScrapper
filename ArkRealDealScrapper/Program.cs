using ArkRealDealScrapper.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<ScanOptions>(context.Configuration.GetSection("Scan"));
        services.Configure<DiscordOptions>(context.Configuration.GetSection("Discord"));

        services.PostConfigure<DiscordOptions>(options =>
        {
            string? env = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
            if (!string.IsNullOrWhiteSpace(env))
            {
                options.WebhookUrl = env;
            }
        });

        services.AddHttpClient();
        services.AddSingleton<ArkRealDealScrapper.Infrastructure.JsonFileLoader>();

        services.AddHostedService<DealScannerWorker>();
    })
    .Build();

await host.RunAsync();