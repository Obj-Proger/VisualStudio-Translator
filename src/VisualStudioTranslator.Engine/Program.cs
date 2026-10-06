using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Caching;
using VisualStudioTranslator.Engine.LocalTranslation;
using VisualStudioTranslator.Engine.Rpc;
using VisualStudioTranslator.Engine.Translation;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Providers are registered here as they are written. A provider whose models are not
// installed simply reports that it does not support a language pair.
builder.Services.AddLocalTranslation();

// Memory in front, disk behind: fast to read, and nothing is lost when the Engine restarts.
builder.Services.AddSingleton<ITranslationCache>(services => new TieredTranslationCache(
    new MemoryTranslationCache(),
    new SqliteTranslationCache(
        SqliteTranslationCache.DefaultPath,
        services.GetRequiredService<ILogger<SqliteTranslationCache>>())));

builder.Services.AddSingleton<TranslationOrchestrator>();
builder.Services.AddSingleton<ITranslatorService, TranslatorService>();
builder.Services.AddHostedService<NamedPipeRpcServer>();

using IHost host = builder.Build();

await host.RunAsync();