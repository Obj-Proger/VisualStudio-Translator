namespace VisualStudioTranslator.Engine.LocalTranslation;

/// <summary>One loaded translation model, ready to translate text in its one direction.</summary>
internal interface ITextTranslator : IDisposable
{
    string Translate(string text);
}

/// <summary>
/// Knows what a model looks like on disk for one particular engine, and how to load it. Kept
/// behind an interface so everything around the engine (where models live, loading them once,
/// serializing access, mapping failures) can be tested without a native library or a
/// 40 MB model.
/// </summary>
internal interface ITextTranslatorFactory
{
    /// <summary>Whether <paramref name="modelDirectory"/> holds a model this engine can load.</summary>
    bool IsModelPresent(string modelDirectory);

    ITextTranslator Create(string modelDirectory);
}