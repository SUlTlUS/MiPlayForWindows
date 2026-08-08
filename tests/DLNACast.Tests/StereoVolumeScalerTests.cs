using DLNACast.Core.Audio;

namespace DLNACast.Tests;

public sealed class StereoVolumeScalerTests
{
    [Fact]
    public void ScaleToMaster_PreservesTheExistingChannelBalance()
    {
        var adjusted = StereoVolumeScaler.ScaleToMaster(25, 11, 50);

        Assert.Equal(50, adjusted.Left, precision: 6);
        Assert.Equal(22, adjusted.Right, precision: 6);
    }

    [Fact]
    public void ScaleToMaster_ScalesDownFromTheLouderChannel()
    {
        var adjusted = StereoVolumeScaler.ScaleToMaster(45, 90, 30);

        Assert.Equal(15, adjusted.Left, precision: 6);
        Assert.Equal(30, adjusted.Right, precision: 6);
    }

    [Fact]
    public void ScaleToMaster_UsesTheRequestedLevelWhenBothChannelsAreMuted()
    {
        var adjusted = StereoVolumeScaler.ScaleToMaster(0, 0, 40);

        Assert.Equal(40, adjusted.Left, precision: 6);
        Assert.Equal(40, adjusted.Right, precision: 6);
    }

    [Fact]
    public void GetMasterVolume_UsesTheLouderChannelAndClampsTheRange()
    {
        Assert.Equal(80, StereoVolumeScaler.GetMasterVolume(35, 80));
        Assert.Equal(100, StereoVolumeScaler.GetMasterVolume(125, 80));
        Assert.Equal(0, StereoVolumeScaler.GetMasterVolume(-20, -10));
    }
}
