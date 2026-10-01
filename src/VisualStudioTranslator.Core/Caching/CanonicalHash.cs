using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VisualStudioTranslator.Core.Caching;

/// <summary>
/// Hashes a sequence of values into a lowercase hex SHA-256. Values are written with
/// <see cref="BinaryWriter"/>, whose strings carry a length prefix, so two different
/// sequences of fields can never serialize to the same bytes ("a" + "bc" versus "ab" + "c").
/// </summary>
internal static class CanonicalHash
{
    // The default BinaryWriter encoding throws on invalid UTF-16 such as a lone surrogate.
    // A translated comment must never be able to break the pipeline, so invalid input is
    // replaced instead; two different invalid strings hashing alike is harmless.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static string Compute(Action<BinaryWriter> write)
    {
        using MemoryStream stream = new();

        using (BinaryWriter writer = new(stream, Utf8, leaveOpen: true))
        {
            write(writer);
        }

        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream.ToArray());

        StringBuilder hex = new(hash.Length * 2);
        foreach (byte b in hash)
        {
            hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }
}