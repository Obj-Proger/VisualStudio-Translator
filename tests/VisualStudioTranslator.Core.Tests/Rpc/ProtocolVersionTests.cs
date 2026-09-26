using AwesomeAssertions;
using VisualStudioTranslator.Core.Rpc;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Rpc;

public sealed class ProtocolVersionTests
{
    [Fact]
    public void IsCompatibleWith_SameMajor_ReturnsTrue()
    {
        ProtocolVersion.IsCompatibleWith(ProtocolVersion.Major).Should().BeTrue();
    }

    [Fact]
    public void IsCompatibleWith_DifferentMajor_ReturnsFalse()
    {
        ProtocolVersion.IsCompatibleWith(ProtocolVersion.Major + 1).Should().BeFalse();
    }
}