using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.LocalTranslation;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation;

// Builds the Engine's services the way Program does, without starting anything: nothing here touches the disk,
// the network or a process until a translation or an installation is asked for.
public sealed class LocalTranslationRegistrationTests : IDisposable
{
    private readonly ServiceProvider _services;

    public LocalTranslationRegistrationTests()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddLocalTranslation();

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Provider_IsTheLanguageModelOnThisMachine()
    {
        ITranslationProvider provider = _services.GetServices<ITranslationProvider>().Should().ContainSingle().Which;

        provider.Should().BeOfType<LlamaTranslationProvider>();
        provider.Info.Id.Should().Be("local");
        provider.Info.Kind.Should().Be(ProviderKind.Local);
    }

    [Fact]
    public void Installer_InstallsTheServerAndTheModel()
    {
        _services.GetRequiredService<IModelInstaller>().Should().BeOfType<LlamaInstaller>();
    }

    [Fact]
    public void Server_IsStartedFromWhereTheInstallerPutsIt()
    {
        LlamaInstallation installation = _services.GetRequiredService<LlamaInstallation>();
        LlamaServerOptions options = _services.GetRequiredService<LlamaServerOptions>();

        options.ExecutablePath.Should().Be(installation.ServerPath);
        options.ModelPath.Should().Be(installation.ModelPath);
    }

    [Fact]
    public void Installation_IsUnderTheUsersLocalApplicationData()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        _services.GetRequiredService<LlamaInstallation>().Root.Should().StartWith(localData);
    }
}