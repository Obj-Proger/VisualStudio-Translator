using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Rpc;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ITranslatorService, TranslatorService>();
builder.Services.AddHostedService<NamedPipeRpcServer>();

using IHost host = builder.Build();

await host.RunAsync();