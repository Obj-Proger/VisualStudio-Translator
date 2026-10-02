using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.Translation;

/// <summary>
/// What <see cref="ProviderSelector"/> decided: a provider to use, or none, and in the "none"
/// case whether the only thing standing in the way was the user's consent to a cloud provider.
/// </summary>
internal sealed record ProviderSelection(ITranslationProvider? Provider, bool CloudConsentRequired);

/// <summary>
/// Chooses which registered provider translates a request. The rule is deliberately simple
/// and favors privacy: a provider that runs on this machine always wins over one that sends
/// text away, and a cloud provider is only chosen when no local one can do the job and the user
/// has agreed. Within each kind, the order of registration decides.
/// </summary>
internal static class ProviderSelector
{
    public static ProviderSelection Select(
        IEnumerable<ITranslationProvider> providers, LanguagePair languages, bool allowCloud)
    {
        ITranslationProvider? firstCloud = null;

        foreach (ITranslationProvider provider in providers)
        {
            if (!provider.Supports(languages))
            {
                continue;
            }

            if (provider.Info.Kind == ProviderKind.Local)
            {
                return new ProviderSelection(provider, CloudConsentRequired: false);
            }

            firstCloud ??= provider;
        }

        if (firstCloud is null)
        {
            return new ProviderSelection(null, CloudConsentRequired: false);
        }

        // A cloud provider could do it. Whether it may is the user's call, and the caller
        // is told that is the only obstacle, so it can ask instead of just giving up.
        return allowCloud
            ? new ProviderSelection(firstCloud, CloudConsentRequired: false)
            : new ProviderSelection(null, CloudConsentRequired: true);
    }
}