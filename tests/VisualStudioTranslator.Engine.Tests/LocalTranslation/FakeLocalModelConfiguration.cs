using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation;

/// <summary>Settings for the local model, for tests: records what it was given and applies nothing.</summary>
internal sealed class FakeLocalModelConfiguration : ILocalModelConfiguration
{
    public List<LocalModelSettings> Applied { get; } = [];

    public Task ApplyAsync(LocalModelSettings settings, CancellationToken cancellationToken)
    {
        Applied.Add(settings);
        return Task.CompletedTask;
    }
}