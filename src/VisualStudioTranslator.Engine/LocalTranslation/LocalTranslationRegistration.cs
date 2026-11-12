using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.LocalTranslation.Downloads;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;

namespace VisualStudioTranslator.Engine.LocalTranslation;

internal static class LocalTranslationRegistration
{
    /// <summary>
    /// Registers the provider that translates on this machine, through a language model run by llama.cpp's server,
    /// and the installer that puts the server and the model in place.
    /// </summary>
    public static IServiceCollection AddLocalTranslation(this IServiceCollection services)
    {
        services.AddSingleton(_ => LlamaInstallation.ForThisMachine());
        services.AddSingleton(provider =>
        {
            LlamaInstallation installation = provider.GetRequiredService<LlamaInstallation>();
            return new LlamaServerOptions { ExecutablePath = installation.ServerPath, ModelPath = installation.ModelPath };
        });

        // Disposed by the container when the Engine stops, which ends the server with it. The job object it
        // holds ends the server even when the Engine does not stop cleanly.
        services.AddSingleton<IServerProcessLauncher, ProcessLauncher>();
        services.AddSingleton(provider => new LlamaServer(
            provider.GetRequiredService<IServerProcessLauncher>(),
            provider.GetRequiredService<LlamaServerOptions>(),
            CreateServerTransport(),
            provider.GetRequiredService<ILogger<LlamaServer>>()));
        services.AddSingleton<ITranslationProvider, LlamaTranslationProvider>();

        services.AddSingleton(_ => new FileDownloader(CreateDownloadClient()));
        services.AddSingleton<IModelInstaller, LlamaInstaller>();

        return services;
    }

    // The server is on this machine, so no proxy may stand between: one would see the text, and most would not
    // reach the loopback address anyway.
    private static SocketsHttpHandler CreateServerTransport() => new() { UseProxy = false };

    // No overall timeout: a model is gigabytes over a connection of any speed. The downloader notices a stall.
    // The system's proxy settings apply here, as they do for every other download on the machine.
    private static HttpClient CreateDownloadClient()
    {
        HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VisualStudioTranslator/1.0");
        return client;
    }
}