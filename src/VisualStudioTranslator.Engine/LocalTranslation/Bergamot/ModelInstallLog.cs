using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Engine.LocalTranslation.Bergamot;

/// <summary>Source-generated log messages for installing models. None quotes anything the user wrote.</summary>
internal static partial class ModelInstallLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Installing the model for {LanguagePair}.")]
    public static partial void ModelInstallStarted(this ILogger logger, LanguagePair languagePair);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installed the {Architecture} model for {LanguagePair}.")]
    public static partial void ModelInstalled(this ILogger logger, LanguagePair languagePair, string architecture);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Installing the model for {LanguagePair} failed.")]
    public static partial void ModelInstallFailed(this ILogger logger, LanguagePair languagePair, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model registry could not be read.")]
    public static partial void RegistryUnreachable(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model registry points at '{Host}', which is not where the registry itself is; it was not used.")]
    public static partial void RegistryHostRejected(this ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Information, Message = "The registry gives no checksum for '{FileName}', so it was accepted on the strength of the connection alone.")]
    public static partial void FileUnverified(this ILogger logger, string fileName);
}