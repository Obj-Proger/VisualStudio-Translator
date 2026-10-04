using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.Translation;

/// <summary>
/// What <see cref="ProviderSelector"/> decided: a provider to use, or none, and in the "none"
/// case whether the only thing standing in the way was the user's consent to a cloud provider.
/// </summary>
internal sealed record ProviderSelection(ITranslationProvider? Provider, bool CloudConsentRequired);

/// <summary>
/// Chooses which registered provider translates a request. The user's consent to cloud
/// translation is read as a request for better quality, because nobody agrees to send their text
/// away to get the same result: with consent, a cloud provider that can do the job wins. Without
/// it text stays on this machine, and a cloud provider is never used. Within each kind, the order
/// of registration decides.
/// </summary>
internal static class ProviderSelector
{
    public static ProviderSelection Select(
        IEnumerable<ITranslationProvider> providers, LanguagePair languages, bool allowCloud)
    {
        ITranslationProvider? firstLocal = null;
        ITranslationProvider? firstCloud = null;

        foreach (ITranslationProvider provider in providers)
        {
            if (!provider.Supports(languages))
            {
                continue;
            }

            if (provider.Info.Kind == ProviderKind.Local)
            {
                firstLocal ??= provider;
            }
            else
            {
                firstCloud ??= provider;
            }
        }

        if (firstCloud is null)
        {
            return new ProviderSelection(firstLocal, CloudConsentRequired: false);
        }

        if (allowCloud)
        {
            return new ProviderSelection(firstCloud, CloudConsentRequired: false);
        }

        // A cloud provider could do it but the user has not agreed. A local one still can, if there is one.
        // Otherwise the caller is told that consent is the only obstacle, so it can ask instead of giving up.
        return firstLocal is not null
            ? new ProviderSelection(firstLocal, CloudConsentRequired: false)
            : new ProviderSelection(null, CloudConsentRequired: true);
    }
}