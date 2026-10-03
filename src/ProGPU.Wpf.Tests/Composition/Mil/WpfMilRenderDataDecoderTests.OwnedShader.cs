using System;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfMilRenderDataDecoderTests
{
    // This is an actual decoder/capability boundary, not a fabricated retained
    // video texture or a claim of immutable video/GPU source support.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedShaderRecordingRejectsVideoBeforeSourceCallback(bool ownedRecording)
    {
        var player = new UncapturedVideoSource(ownedRecording);
        var resolver = new WpfResourceResolver(new RecordingCapability(ownedRecording));
        resolver.Register(1, player);
        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 8, 9);
        WriteUInt32(payload, 32, 1);
        var sink = new VideoTestSink();

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawVideo, payload), sink, resolver);

        Assert.Equal(ownedRecording ? 1 : 0, result.UnsupportedCount);
        Assert.Equal(ownedRecording ? 0 : 1, result.AppliedCount);
        Assert.Equal(ownedRecording ? 0 : 1, player.ReadCount);
        Assert.Equal(ownedRecording ? 0 : 1, sink.Videos.Count);
    }

    private sealed class RecordingCapability(bool ownedRecording)
        : IWpfImageSourceAdapter, IWpfShaderRecordingAdapterSource
    {
        public bool RecordsOwnedShaderImages => ownedRecording;
        public ImageSource? AdaptImageSource(object? source) =>
            throw new InvalidOperationException("Video must not use the image adapter.");
        public WpfShaderRecordingImageSourceAdapter? CreateShaderRecordingAdapter() =>
            throw new InvalidOperationException("Decoder admission must not create a recording.");
    }

    private sealed class UncapturedVideoSource(bool poisonRead) : IPortableMediaPlayerSource
    {
        public int ReadCount { get; private set; }
        public bool TryGetPortableMediaPlayerFrame(out PortableMediaPlayerFrame frame)
        {
            ReadCount++;
            if (poisonRead) throw new InvalidOperationException("Mutable video callback was invoked.");
            frame = new PortableMediaPlayerFrame(8, 9, 1, this);
            return true;
        }
    }
}
