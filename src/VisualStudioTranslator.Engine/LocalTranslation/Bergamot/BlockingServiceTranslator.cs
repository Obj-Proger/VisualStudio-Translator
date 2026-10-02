using BergamotTranslatorSharp;

namespace VisualStudioTranslator.Engine.LocalTranslation.Bergamot;

/// <summary>
/// The only place that touches the wrapper library's own types. Everything else in the Engine
/// sees <see cref="ITextTranslator"/>, so a change to the wrapper (or replacing it with a fork)
/// is confined to this file and its factory.
/// </summary>
internal sealed class BlockingServiceTranslator(BlockingService service) : ITextTranslator
{
    public string Translate(string text) => service.Translate(text);

    public void Dispose() => service.Dispose();
}