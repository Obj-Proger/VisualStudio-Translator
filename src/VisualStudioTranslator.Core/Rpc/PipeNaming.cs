namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// Builds the named pipe name that the Engine listens on and the Vsix client connects
/// to. Kept here, rather than duplicated on each side, so a future change to the format
/// cannot make the two sides disagree. Callers supply the current user's SID themselves;
/// obtaining it is a platform concern this library does not take on.
/// </summary>
public static class PipeNaming
{
    public static string GetPipeName(string userSid) =>
        $"VisualStudioTranslator-{userSid}-v{ProtocolVersion.Major}";
}