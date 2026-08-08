using System.Buffers.Binary;
using System.Net;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPassiveSenderCaptureProfileTests
{
    [Fact]
    public void DefaultProfilePublishesDistinctParseableMiPlayIdentity()
    {
        var address = IPAddress.Parse("192.168.10.9");
        var profile = MiPlayPassiveSenderCaptureProfile.CreateDefault(address);

        var device = Assert.Single(MiPlayMdnsMessageParser.Parse(profile.BuildMdnsAnnouncement()));

        Assert.Equal(MiPlayPassiveSenderCaptureProfile.DefaultFriendlyName, device.FriendlyName);
        Assert.Equal(profile.InstanceName, device.InstanceName);
        Assert.Equal(profile.HostName, device.HostName);
        Assert.Equal(address, device.Address);
        Assert.Equal(MiPlayPassiveSenderCaptureProfile.AdvertisedCoapPort, device.Port);
        Assert.Equal("[5]", device.TxtRecords["apps"]);
        Assert.NotEqual("小爱音箱-6333", device.FriendlyName);
        Assert.NotEqual("小爱音箱-7503", device.FriendlyName);

        var capabilities = MiPlayMdnsCapabilities.Parse(device.TxtRecords);
        Assert.Equal(1155, capabilities.MiPlayAudioAppData?.ControlPort);
        Assert.False(capabilities.MiPlayAudioAppData?.SupportsLyra);
        Assert.Equal(MiPlayPassiveSenderCaptureProfile.DefaultDeviceId.ToString("D"), capabilities.MicoAppData?.DeviceId);

        var appData = Convert.FromBase64String(device.TxtRecords["appsData"]);
        Assert.Equal(
            MiPlayPassiveSenderCaptureProfile.AdvertisedControlPort,
            BinaryPrimitives.ReadUInt16BigEndian(appData.AsSpan(5, 2)));
        Assert.Equal(0, appData[7] & 0x01);
        Assert.Equal(0x02, appData[7] & 0x02);
    }

    [Fact]
    public void CaptureProfileOnlyPermitsVerifiedLegacyChallengeOutbound()
    {
        var profile = MiPlayPassiveSenderCaptureProfile.CreateDefault(IPAddress.Parse("192.168.10.9"));
        var frame = profile.BuildLegacyChallengeFrame();

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frame, out var decoded, out var consumed));
        Assert.NotNull(decoded);
        Assert.Equal(frame.Length, consumed);
        Assert.Equal(MiPlayProtocolConstants.LegacySafetyChallengeCommand, decoded.Command);
        Assert.Equal(MiPlayPassiveSenderCaptureProfile.ChallengeSequence, decoded.Sequence);
        Assert.Equal(MiPlayPassiveSenderCaptureProfile.ChallengeText, Encoding.ASCII.GetString(decoded.Payload));
        Assert.True(MiPlayPassiveSenderCaptureProfile.IsPermittedOutboundCommand(decoded.Command));

        Assert.False(MiPlayPassiveSenderCaptureProfile.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand));
        Assert.False(MiPlayPassiveSenderCaptureProfile.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SafetyAuthCommand));
        Assert.False(MiPlayPassiveSenderCaptureProfile.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SetPlaySourceCommand));
        Assert.False(MiPlayPassiveSenderCaptureProfile.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.OpenDeviceCommand));
    }
}
