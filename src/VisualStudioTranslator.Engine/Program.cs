using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Caching;
using VisualStudioTranslator.Engine.Rpc;
using VisualStudioTranslator.Engine.Translation;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Providers are registered here as they are written; with none registered the service
// answers every translation request with NoProviderAvailable.
builder.Services.AddSingleton<ITranslationCache, MemoryTranslationCache>();
builder.Services.AddSingleton<TranslationOrchestrator>();
builder.Services.AddSingleton<ITranslatorService, TranslatorService>();
builder.Services.AddHostedService<NamedPipeRpcServer>();

using IHost host = builder.Build();

await host.RunAsync();