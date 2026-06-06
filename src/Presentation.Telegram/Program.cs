using Application.Messaging;
using Infrastructure.Caching;
using Infrastructure.Blockchain;
using Infrastructure.Persistence;
using Infrastructure.Workers;
using Application.Behaviors;
using Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Presentation.Telegram.Configuration;
using Presentation.Telegram.Workers;
using Telegram.Bot;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.Configure<BlockchainIndexerOptions>(
    builder.Configuration.GetSection("BlockchainIndexer"));

builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection("TelegramBot"));

var connectionString = builder.Configuration["ConnectionStrings:Default"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<Application.Abstractions.ITrackedWalletRepository, TrackedWalletRepository>();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<AssemblyReference>();
    cfg.AddOpenBehavior(typeof(RequestLoggingPipelineBehavior<,>));
});

builder.Services.AddSingleton<OnChainPipelineChannels>();
builder.Services.AddSingleton<InMemoryCacheService>();
builder.Services.AddSingleton<Application.Abstractions.ITrackedAddressCacheService>(sp => sp.GetRequiredService<InMemoryCacheService>());
builder.Services.AddSingleton<Application.Abstractions.ITrackedWalletCacheService>(sp => sp.GetRequiredService<InMemoryCacheService>());

var blockchainIndexerSection = builder.Configuration.GetSection("BlockchainIndexer");
var httpRpcUrl = blockchainIndexerSection["HttpRpcUrl"];
if (!string.IsNullOrWhiteSpace(httpRpcUrl))
{
    builder.Services.AddSingleton<IBlockchainBlockSource, NethereumBlockchainBlockSource>();
    builder.Services.AddHostedService<BlockchainIndexerWorker>();
    builder.Services.AddHostedService<WalletTrackerWorker>();
}
else
{
    Console.WriteLine("BlockchainIndexer:HttpRpcUrl is not configured. Blockchain workers are disabled.");
}

var telegramSection = builder.Configuration.GetSection("TelegramBot");
var botToken = telegramSection["BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
    builder.Services.AddHostedService<NotificationDispatcherWorker>();
}
else
{
    Console.WriteLine("TelegramBot:BotToken is not configured. Notification dispatcher is disabled.");
}

var host = builder.Build();
await host.RunAsync();
