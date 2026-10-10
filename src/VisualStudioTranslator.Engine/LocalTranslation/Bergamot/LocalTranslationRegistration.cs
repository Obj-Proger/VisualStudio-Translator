using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Providers.Abstractions;

// The folder says "Bergamot" but the namespace deliberately does not: this is the one entry
// point the rest of the Engine calls, and it must not have to name the engine to do so.
#pragma warning disable IDE0130
namespace VisualStudioTranslator.Engine.LocalTranslation;
#pragma warning restore IDE0130

internal static class LocalTranslationRegistration
{
    /// <summary>Registers the provider that translates on this machine, and the installer that fetches its models, using the default folder.</summary>
    public static IServiceCollection AddLocalTranslation(this IServiceCollection services)
    {
        services.AddSingleton(new LocalModelStore(LocalModelStore.DefaultRoot));
        services.AddSingleton<ITextTranslatorFactory, Bergamot.BlockingServiceTranslatorFactory>();
        services.AddSingleton<ITranslationProvider, LocalTranslationProvider>();
        services.AddSingleton<IModelInstaller>(provider => new Bergamot.RegistryModelInstaller(
            CreateHttpClient(),
            provider.GetRequiredService<LocalModelStore>(),
            provider.GetRequiredService<ITextTranslatorFactory>(),
            provider.GetRequiredService<ILogger<Bergamot.RegistryModelInstaller>>()));

        return services;
    }

    // No overall timeout on the client: a model is a large download over a connection of any speed, so
    // the installer limits the whole installation instead.
    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VisualStudioTranslator/1.0");
        return client;
    }
}