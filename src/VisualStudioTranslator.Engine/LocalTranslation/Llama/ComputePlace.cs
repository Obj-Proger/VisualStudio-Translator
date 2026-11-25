namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>Where the server computes: one graphics card, whichever card the server picks, or the processor.</summary>
/// <param name="DeviceArgument">What goes after <c>--device</c>, or <see langword="null"/> to leave it to the server.</param>
/// <param name="Device">
/// How a failure here is remembered (see <see cref="GpuVerdict"/>): the card's name, or empty for any card.
/// </param>
/// <param name="GpuLayers">How many of the model's layers go to the card.</param>
/// <param name="Description">What the log calls it.</param>
internal sealed record ComputePlace(string? DeviceArgument, string Device, int GpuLayers, string Description)
{
    /// <summary>The processor alone: no card, whatever cards there are.</summary>
    public static ComputePlace Processor { get; } = new("none", string.Empty, 0, "the processor");

    public bool UsesGpu => GpuLayers > 0;

    /// <summary>Whichever card the server picks, as it would when left to itself.</summary>
    public static ComputePlace AnyCard(int gpuLayers) => new(null, string.Empty, gpuLayers, "the graphics card");

    /// <summary>This card and no other.</summary>
    public static ComputePlace Card(GpuDevice device, int gpuLayers) => new(device.Id, device.Name, gpuLayers, device.Name);
}