using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.LocalTranslation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation;

public sealed class LocalTranslationProviderTests : IDisposable
{
    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("vst-provider-");

    public void Dispose() => _root.Delete(recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LocalTranslationProvider Provider(FakeFactory factory) =>
        new(new LocalModelStore(_root.FullName), factory, NullLogger<LocalTranslationProvider>.Instance);

    [Fact]
    public void Info_DescribesALocalProviderRevisedByTheInstalledModels()
    {
        File.WriteAllBytes(Path.Combine(_root.FullName, "model.bin"), new byte[10]);
        using LocalTranslationProvider provider = Provider(new FakeFactory());

        provider.Info.Id.Should().Be("local");
        provider.Info.Kind.Should().Be(ProviderKind.Local);
        provider.Info.Revision.Should().MatchRegex("^[0-9a-f]{12}$");
    }

    [Fact]
    public void Supports_FollowsWhetherAModelIsPresent()
    {
        FakeFactory factory = new();
        using LocalTranslationProvider provider = Provider(factory);

        factory.Present = true;
        provider.Supports(EnglishToRussian).Should().BeTrue();

        factory.Present = false;
        provider.Supports(EnglishToRussian).Should().BeFalse();
    }

    [Fact]
    public async Task TranslateAsync_TranslatesEveryTextInOrder()
    {
        using LocalTranslationProvider provider = Provider(new FakeFactory());

        IReadOnlyList<string> result = await provider.TranslateAsync(EnglishToRussian, ["one", "two", "three"], Token);

        result.Should().Equal("ONE", "TWO", "THREE");
    }

    [Fact]
    public async Task TranslateAsync_LoadsTheModelOnceHoweverOftenItIsUsed()
    {
        FakeFactory factory = new();
        using LocalTranslationProvider provider = Provider(factory);

        await provider.TranslateAsync(EnglishToRussian, ["a"], Token);
        await provider.TranslateAsync(EnglishToRussian, ["b"], Token);
        await Task.WhenAll(
            provider.TranslateAsync(EnglishToRussian, ["c"], Token),
            provider.TranslateAsync(EnglishToRussian, ["d"], Token));

        factory.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_EmptyInput_ReturnsNothingWithoutLoadingAModel()
    {
        FakeFactory factory = new();
        using LocalTranslationProvider provider = Provider(factory);

        (await provider.TranslateAsync(EnglishToRussian, [], Token)).Should().BeEmpty();

        factory.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task TranslateAsync_NoModelInstalled_ThrowsUnsupportedLanguagePair()
    {
        using LocalTranslationProvider provider = Provider(new FakeFactory { Present = false });

        Func<Task> act = () => provider.TranslateAsync(EnglishToRussian, ["a"], Token);

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.UnsupportedLanguagePair);
    }

    [Fact]
    public async Task TranslateAsync_ModelThatFailsToLoad_ThrowsUnavailableAndIsRetriedLater()
    {
        FakeFactory factory = new() { CreateTranslator = () => throw new InvalidOperationException("corrupt model") };
        using LocalTranslationProvider provider = Provider(factory);

        Func<Task> failing = () => provider.TranslateAsync(EnglishToRussian, ["a"], Token);
        (await failing.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.Unavailable);

        // The failure was not remembered: once the problem is fixed, the next request works.
        factory.CreateTranslator = () => new FakeTranslator(text => text.ToUpperInvariant());
        (await provider.TranslateAsync(EnglishToRussian, ["a"], Token)).Should().Equal("A");
    }

    [Fact]
    public async Task TranslateAsync_EngineFailingOnASegment_ThrowsInvalidResponse()
    {
        FakeFactory factory = new()
        {
            CreateTranslator = () => new FakeTranslator(_ => throw new InvalidOperationException("native failure")),
        };
        using LocalTranslationProvider provider = Provider(factory);

        Func<Task> act = () => provider.TranslateAsync(EnglishToRussian, ["a"], Token);

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.InvalidResponse);
    }

    [Fact]
    public async Task TranslateAsync_ConcurrentCalls_NeverRunTheEngineInParallel()
    {
        FakeTranslator translator = new(text => text.ToUpperInvariant());
        using LocalTranslationProvider provider = Provider(new FakeFactory { CreateTranslator = () => translator });

        await Task.WhenAll(Enumerable.Range(0, 6).Select(
            i => provider.TranslateAsync(EnglishToRussian, [$"a{i}", $"b{i}"], Token)));

        translator.MaxConcurrent.Should().Be(1);
        translator.Seen.Should().HaveCount(12);
    }

    [Fact]
    public async Task TranslateAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using LocalTranslationProvider provider = Provider(new FakeFactory());
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => provider.TranslateAsync(EnglishToRussian, ["a"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Dispose_DisposesTheLoadedTranslators()
    {
        FakeTranslator translator = new(text => text);
        LocalTranslationProvider provider = Provider(new FakeFactory { CreateTranslator = () => translator });
        await provider.TranslateAsync(EnglishToRussian, ["a"], Token);

        provider.Dispose();

        translator.Disposed.Should().BeTrue();
    }

    private sealed class FakeTranslator(Func<string, string> translate) : ITextTranslator
    {
        private int _active;

        public int MaxConcurrent { get; private set; }

        public bool Disposed { get; private set; }

        public List<string> Seen { get; } = [];

        public string Translate(string text)
        {
            int active = Interlocked.Increment(ref _active);

            lock (Seen)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, active);
                Seen.Add(text);
            }

            try
            {
                Thread.Sleep(5); // long enough for an overlap to show if one were possible
                return translate(text);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory : ITextTranslatorFactory
    {
        private int _createCalls;

        public bool Present { get; set; } = true;

        public Func<ITextTranslator> CreateTranslator { get; set; } = () => new FakeTranslator(text => text.ToUpperInvariant());

        public int CreateCalls => _createCalls;

        public bool IsModelPresent(string modelDirectory) => Present;

        public ITextTranslator Create(string modelDirectory)
        {
            Interlocked.Increment(ref _createCalls);
            return CreateTranslator();
        }
    }
}