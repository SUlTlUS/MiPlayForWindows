using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayMainApplicationLiveValidationEvidenceTests
{
    [Fact]
    public void PinsTheSuccessfulSingleTargetApplicationRun()
    {
        var snapshot = MiPlayMainApplicationLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Equal("小爱音箱-7503 · S12", snapshot.ReceiverSelectionLabel);
        Assert.Equal("192.168.10.3", snapshot.ReceiverAddress);
        Assert.Equal(600_000, snapshot.AdvertisedDurationMilliseconds);
        Assert.Equal(49, snapshot.FirstAudioPcmPayloadLength);
        Assert.Equal(1, snapshot.FirstAudioPcmValue);
        Assert.Equal(0, snapshot.FirstAudioPcmBufferTime);
        Assert.Equal(2, snapshot.ReceiverPlayingState);
        Assert.Null(snapshot.UserConfirmedAudibleAtReceiver);
        Assert.True(MiPlayMainApplicationLiveValidationEvidence.ProvesMainApplicationTransportReady(snapshot));
    }
}
