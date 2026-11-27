using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.SignatureHelp;

// TEMPORARY, to be removed once signature help is translated: it only writes to the extension's log what the
// signature help popup is made of, for every language, so that the way to translate it is chosen from facts rather
// than guesses. It changes nothing in the popup. It logs shapes, never text: the kinds of signature, how long their
// documentation is, how many parameters have documentation of their own.
[Export(typeof(ISignatureHelpSourceProvider))]
[Name("Visual Studio Translator Signature Help Probe")]
[Order(After = "default")]
[ContentType("any")]
internal sealed class SignatureHelpProbeProvider : ISignatureHelpSourceProvider
{
    public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer) => new SignatureHelpProbe(textBuffer);
}

internal sealed class SignatureHelpProbe(ITextBuffer buffer) : ISignatureHelpSource
{
    private const string Source = nameof(SignatureHelpProbe);

    private static readonly HashSet<string> Described = [];

    public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
    {
        try
        {
            string kinds = string.Join(", ", signatures.Select(signature => signature.GetType().FullName).Distinct());
            string documentation = string.Join(", ", signatures.Select(signature =>
                $"{signature.Documentation?.Length ?? 0} chars, "
                + $"{signature.Parameters?.Count(parameter => !string.IsNullOrWhiteSpace(parameter.Documentation)) ?? 0}"
                + $"/{signature.Parameters?.Count ?? 0} parameters documented"));
            string shape = $"content type {buffer.ContentType.TypeName}; {signatures.Count} signatures of {kinds}; {documentation}";

            lock (Described)
            {
                if (!Described.Add(shape))
                {
                    return;
                }
            }

            DiagnosticLog.Info(Source, $"Signature help: {shape}.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(Source, $"Reading signature help failed: {ex}");
        }
    }

    public ISignature? GetBestMatch(ISignatureHelpSession session) => null;

    public void Dispose()
    {
    }
}