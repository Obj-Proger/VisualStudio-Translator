namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// Which language model translates on this machine. Sent as a number, so values are never renumbered or reused:
/// add new ones at the end.
/// </summary>
public enum LocalModelChoice
{
    /// <summary>The one the machine's memory suits: the quality model with about 16 GB or more, the light one below.</summary>
    Automatic = 0,

    /// <summary>The better translations, at the cost of more memory and a larger download.</summary>
    Quality = 1,

    /// <summary>Less memory and a smaller download, at some cost to the translations.</summary>
    Light = 2,
}

/// <summary>
/// How the user wants translation on this machine to run. One Engine serves every Visual Studio the user has
/// open, so the settings it was sent last apply to all of them.
/// Added in protocol version 1.3.
/// </summary>
public sealed record LocalModelSettings
{
    /// <summary>
    /// How each <see cref="LocalModelChoice"/> is stored in the extension's settings, in the order of its values.
    /// Words rather than numbers, so a stored setting reads as what it means.
    /// </summary>
    public static IReadOnlyList<string> ModelTags { get; } = ["auto", "quality", "light"];

    public LocalModelChoice Model { get; init; } = LocalModelChoice.Automatic;

    /// <summary>
    /// Whether the model may run on a graphics card, which is several times faster. Without it the processor
    /// does the work and the card stays free for whatever else needs it.
    /// </summary>
    public bool UseGraphicsCard { get; init; } = true;

    /// <summary>
    /// The choice a stored tag stands for; one this version does not know is <see cref="LocalModelChoice.Automatic"/>.
    /// </summary>
    public static LocalModelChoice ParseModel(string? tag)
    {
        for (int index = 0; index < ModelTags.Count; index++)
        {
            if (string.Equals(ModelTags[index], tag, StringComparison.OrdinalIgnoreCase))
            {
                return (LocalModelChoice)index;
            }
        }

        return LocalModelChoice.Automatic;
    }
}