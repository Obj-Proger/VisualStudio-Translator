namespace VisualStudioTranslator.Engine.Tests.Support;

/// <summary>A clock that only moves when a test says so.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}