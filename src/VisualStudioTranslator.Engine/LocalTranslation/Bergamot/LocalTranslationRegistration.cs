using Microsoft.Extensions.DependencyInjection;
using VisualStudioTranslator.Core.Providers.Abstractions;

// The folder says "Bergamot" but the namespace deliberately does not: this is the one entry
// point the rest of the Engine calls, and it must not have to name the engine to do so.
#pragma warning disable IDE0130
namespace VisualStudioTranslator.Engine.LocalTranslation;
#pragma warning restore IDE0130

internal static class LocalTranslationRegistration
{
    /// <summary>Registers the provider that translates on this machine, using the models in the default folder.</summary>
    public static IServiceCollection AddLocalTranslation(this IServiceCollection services)
    {
        services.AddSingleton(new LocalModelStore(LocalModelStore.DefaultRoot));
        services.AddSingleton<ITextTranslatorFactory, Bergamot.BlockingServiceTranslatorFactory>();
        services.AddSingleton<ITranslationProvider, LocalTranslationProvider>();

        return services;
    }
}