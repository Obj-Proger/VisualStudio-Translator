namespace VisualStudioTranslator.Vsix.Setup;

/// <summary>
/// Remembers the languages the user said "don't ask again" about, so a model is never offered
/// twice against their wishes. A plain file in the user's profile, one language per line: there is
/// nothing here worth a settings store, and it can be edited or deleted by hand to be asked again.
/// Language tags are kept in lowercase, so "ru-RU" and "ru-ru" are the same language.
/// </summary>
internal static class DeclinedModelPrompts
{
    private static readonly object Sync = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudioTranslator",
        "declined-models.txt");

    public static bool IsDeclined(string language)
    {
        string key = Normalize(language);

        lock (Sync)
        {
            return Read().Contains(key);
        }
    }

    public static void Decline(string language)
    {
        string key = Normalize(language);

        lock (Sync)
        {
            HashSet<string> declined = Read();

            if (!declined.Add(key))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllLines(FilePath, declined);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not remembered; the user is asked again next time, which is the safe direction to fail in.
            }
        }
    }

    private static string Normalize(string language) => language.Trim().ToLowerInvariant();

    private static HashSet<string> Read()
    {
        HashSet<string> declined = [];

        try
        {
            if (File.Exists(FilePath))
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string key = Normalize(line);

                    if (key.Length > 0)
                    {
                        declined.Add(key);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable file means nothing has been declined.
        }

        return declined;
    }
}