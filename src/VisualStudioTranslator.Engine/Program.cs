using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Caching;
using VisualStudioTranslator.Engine.Lifecycle;
using VisualStudioTranslator.Engine.LocalTranslation;
using VisualStudioTranslator.Engine.Logging;
using VisualStudioTranslator.Engine.Rpc;
using VisualStudioTranslator.Engine.Translation;

// One Engine per user. The extension starts one whenever it finds none, so two Visual Studio
// instances opening together, or a quick restart, can start two: the later one finds the
// first and has nothing to do. The name is derived from the pipe's, so an Engine of another
// protocol version, which listens elsewhere, is never turned away by this one.
using SingleInstanceGuard? instance = SingleInstanceGuard.TryAcquire("Local\\" + NamedPipeRpcServer.PipeName);
if (instance is null)
{
    return;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// The Engine has no window, so its log goes to a file.
builder.Logging.AddProvider(new FileLoggerProvider(FileLoggerProvider.DefaultPath));

// Providers are registered here as they are written. A provider whose models are not
// installed simply reports that it does not support a language pair.
builder.Services.AddLocalTranslation();

// Memory in front, disk behind: fast to read, and nothing is lost when the Engine restarts.
builder.Services.AddSingleton<ITranslationCache>(services => new TieredTranslationCache(
    new MemoryTranslationCache(),
    new SqliteTranslationCache(
        SqliteTranslationCache.DefaultPath,
        services.GetRequiredService<ILogger<SqliteTranslationCache>>())));

// The Engine ends itself when no client has been connected for a while.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new EngineLifetimeOptions());
builder.Services.AddSingleton<ClientTracker>();
builder.Services.AddHostedService<IdleShutdownService>();

builder.Services.AddSingleton<TranslationOrchestrator>();
builder.Services.AddSingleton<ITranslatorService, TranslatorService>();
builder.Services.AddHostedService<NamedPipeRpcServer>();

using IHost host = builder.Build();

await host.RunAsync();