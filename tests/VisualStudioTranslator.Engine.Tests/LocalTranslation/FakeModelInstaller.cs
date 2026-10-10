using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation;

/// <summary>An installer for tests that records what it was asked and answers with a fixed status.</summary>
internal sealed class FakeModelInstaller : IModelInstaller
{
    public ModelInstallStatus Status { get; set; } = new() { State = ModelInstallState.NotInstalled, SourceHost = "models.example.test" };

    public List<LanguagePair> StatusRequests { get; } = [];

    public List<LanguagePair> StartRequests { get; } = [];

    public Task<ModelInstallStatus> GetStatusAsync(LanguagePair pair, CancellationToken cancellationToken)
    {
        StatusRequests.Add(pair);
        return Task.FromResult(Status);
    }

    public Task<ModelInstallStatus> StartInstallAsync(LanguagePair pair, CancellationToken cancellationToken)
    {
        StartRequests.Add(pair);
        return Task.FromResult(Status with { State = ModelInstallState.Installing });
    }
}