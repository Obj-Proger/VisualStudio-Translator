using BergamotTranslatorSharp;

namespace VisualStudioTranslator.Engine.LocalTranslation.Bergamot;

internal sealed class BlockingServiceTranslatorFactory : ITextTranslatorFactory
{
    public bool IsModelPresent(string modelDirectory)
    {
        try
        {
            if (!Directory.Exists(modelDirectory))
            {
                return false;
            }

            // Either a config is already there, or the files are complete enough to make one.
            return File.Exists(Path.Combine(modelDirectory, BergamotModelConfig.FileName))
                || BergamotModelConfig.Build(ListFileNames(modelDirectory)) is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public ITextTranslator Create(string modelDirectory)
    {
        string configPath = Path.Combine(modelDirectory, BergamotModelConfig.FileName);

        if (!File.Exists(configPath))
        {
            string config = BergamotModelConfig.Build(ListFileNames(modelDirectory))
                ?? throw new InvalidOperationException("The directory does not contain a complete model.");

            File.WriteAllText(configPath, config);
        }

        return new BlockingServiceTranslator(new BlockingService(configPath));
    }

    private static IEnumerable<string> ListFileNames(string directory) =>
        Directory.EnumerateFiles(directory).Select(path => Path.GetFileName(path)).OfType<string>();
}