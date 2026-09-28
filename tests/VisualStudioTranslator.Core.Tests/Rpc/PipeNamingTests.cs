using AwesomeAssertions;
using VisualStudioTranslator.Core.Rpc;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Rpc;

public sealed class PipeNamingTests
{
    [Fact]
    public void GetPipeName_IncludesSidAndProtocolMajor()
    {
        string name = PipeNaming.GetPipeName("S-1-5-21-1-2-3-1001");

        name.Should().Be($"VisualStudioTranslator-S-1-5-21-1-2-3-1001-v{ProtocolVersion.Major}");
    }

    [Fact]
    public void GetPipeName_DifferentSids_ProduceDifferentNames()
    {
        string a = PipeNaming.GetPipeName("S-1-5-21-1-2-3-1001");
        string b = PipeNaming.GetPipeName("S-1-5-21-1-2-3-1002");

        a.Should().NotBe(b);
    }
}