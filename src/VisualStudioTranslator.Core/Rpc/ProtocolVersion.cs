namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// The wire protocol version implemented by this build. Bumping <see cref="Major"/> is a
/// breaking change and requires the named pipe name to change as well; bumping
/// <see cref="Minor"/> only adds optional fields or methods that older peers can safely ignore.
/// </summary>
public static class ProtocolVersion
{
    public const int Major = 1;

    /// <summary>1: added <c>TranslateDocumentationAsync</c>. 2: added the model installation methods.</summary>
    public const int Minor = 2;

    /// <summary>
    /// Whether a peer advertising <paramref name="otherMajor"/> as its major version
    /// can talk to this build.
    /// </summary>
    public static bool IsCompatibleWith(int otherMajor) => otherMajor == Major;
}