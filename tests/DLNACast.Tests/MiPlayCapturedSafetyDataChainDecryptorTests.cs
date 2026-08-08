using System.Net;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayCapturedSafetyDataChainDecryptorTests
{
    [Fact]
    public void MidSessionRootPcapDecryptsOfficialPostAuthPlaintextsAfterFirstDirectionalFrame()
    {
        var session = new MiPlayTcpSessionInfo(
            IPAddress.Parse("192.168.10.20"),
            43720,
            IPAddress.Parse("192.168.10.7"),
            8899);
        var authKey = session.DeriveType1SafetyKey();
        var decryptor = new MiPlayCapturedSafetyDataChainDecryptor(
            Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
                authKey,
                MiPlaySafetyKeyDerivation.FirstHalfMaterialType)));
        var decrypted = new Dictionary<int, MiPlayCapturedSafetyDataDecryptResult>();

        Assert.Equal(MiPlayRealPhonePostAuthPlaintextEvidence.AuthKeyType1, authKey);

        foreach (var frame in CapturedFrames)
        {
            Assert.True(decryptor.TryDecryptVersion1Continuation(
                frame.Direction,
                Convert.FromHexString(frame.SafetyDataPayloadHex),
                out var result));
            Assert.NotNull(result);
            decrypted.Add(frame.Index, result);
        }

        Assert.False(decrypted[1].FirstBlockKnown);
        Assert.Equal(16, decrypted[1].KnownPlaintextOffset);
        Assert.Contains(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoKnownSuffix,
            Encoding.UTF8.GetString(decrypted[1].KnownPlaintext),
            StringComparison.Ordinal);
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoJson,
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoKnownFirstBlock +
            Encoding.UTF8.GetString(decrypted[1].KnownPlaintext));

        Assert.Empty(decrypted[2].KnownPlaintext);
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.SetLocalCanAlonePlayCtrlJson,
            Encoding.UTF8.GetString(decrypted[4].KnownPlaintext));
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.SetLocalAlonePlayCapacityJson,
            Encoding.UTF8.GetString(decrypted[5].KnownPlaintext));

        Assert.True(MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(
            decrypted[6].KnownPlaintext,
            out var deviceInfo,
            out var bytesConsumed));
        Assert.NotNull(deviceInfo);
        Assert.Equal(decrypted[6].KnownPlaintext.Length, bytesConsumed);
        Assert.Equal("1", deviceInfo.GetValue("alonePlayCapacity"));
        Assert.Equal("1", deviceInfo.GetValue("canAlonePlayCtrl"));
        Assert.Equal("center", deviceInfo.GetValue("channel"));
        Assert.Equal("4", deviceInfo.GetValue("deviceType"));
        Assert.Equal("audio", deviceInfo.GetValue("support"));

        Assert.Empty(decrypted[7].KnownPlaintext);
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.GetMirrorModeAcknowledgementHex,
            Convert.ToHexString(decrypted[14].KnownPlaintext));
        Assert.Empty(decrypted[8].KnownPlaintext);
        Assert.Empty(decrypted[13].KnownPlaintext);
        Assert.Empty(decrypted[15].KnownPlaintext);
        Assert.Empty(decrypted[16].KnownPlaintext);
        Assert.Empty(decrypted[17].KnownPlaintext);
        Assert.Empty(decrypted[18].KnownPlaintext);
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.OfficialSetPlaySourceJson,
            Encoding.UTF8.GetString(decrypted[21].KnownPlaintext));
    }

    [Fact]
    public void PlaintextEvidenceSeparatesRecoveredPayloadFromReplayPermission()
    {
        var snapshot = MiPlayRealPhonePostAuthPlaintextEvidence.CreateSnapshot();
        var decision = MiPlayRealPhonePostAuthPlaintextEvidence.Evaluate(snapshot);

        Assert.True(decision.CanProceed);
        Assert.False(snapshot.SafeForNetworkReplay);
        Assert.Contains("0x0058 source-context JSON", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("GetMirrorMode_Ack value of 2", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("controlcenter/single_room/music_qq", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not permission to replay", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OfficialFirstSourceIdentityPayloadIsFullyRecoveredAndMatchesCapturedLength()
    {
        var payload = MiPlayLocalDeviceInfoPayloadCodec.EncodeRecoveredOfficialSourceIdentity();

        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoJson,
            Encoding.UTF8.GetString(payload));
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoPlaintextLength,
            payload.Length);
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoPaddingLength,
            16 - payload.Length % 16);
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoCiphertextLength,
            payload.Length + MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoPaddingLength);
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoSafetyDataPayloadLength,
            MiPlayRealPhonePostAuthPlaintextEvidence.SafetyDataVersion1HeaderLength +
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoCiphertextLength);

        var defaultProbePayload = MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(
            "DLNACast Windows",
            bluetoothMac: null,
            includeControlFields: false);
        Assert.Equal(51, defaultProbePayload.Length);
        Assert.NotEqual(payload.Length, defaultProbePayload.Length);
    }

    private static readonly CapturedSafetyDataFrame[] CapturedFrames =
    [
        new(
            1,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            0x013A,
            "000701E010DB25F5F0506A7B10C34713B8431C29A8E23BECE26632F1E43941D4C2595C72B4DA8D2870978F5A389D041BE49E8CB6E2AB6118059E99A6C1145971C079FEB9BE938CC9DE64DDA10F6A796C416E24E41052884A45E2FC5E4F3B87EC8337F04DAA04758E6F"),
        new(
            2,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            0x013B,
            "000701E010480566FE54B92702289A684FAAB07D67607453DA"),
        new(
            3,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            0x013A,
            "000701E0108B20A260B93D7C470ABC88BB88390C52E2BFE6EA"),
        new(
            4,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            0x013C,
            "000701E008A1B0F4039FB2E55D803B8C58530FB9CFD13C4FB9E56999D8ABC7D3D265D78C2A912A6587"),
        new(
            5,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            0x013D,
            "000701E0072BBA769CDF7AF8A84CCB355546646F2BDFA4F5D57D5BA47E675A32347D75F7B6C924F1BE"),
        new(
            6,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            0x013B,
            "000701E001205BE7F031A823B9191A8216E9C73749E3907C01057EF77562DF96CD32D741EF3E4E008FC16D28456809A6B1DB74B3142DC7DCC54C89E674E58F9FBEA97E01A0A5D5D1AB3BA5AF7C0080A90C8B0665A56DE32BD2A021FB388447A4CCFF3F478ED8574E2DC634709E75B0AF915A63EFBD3B4EAD762EA80F8B5D02AA091FC460018BA2F088F3DA05F2B4F1896C1B1FF175E0C9E9DE6B2CA7DC816AA6D45DFD113FC534D7E5A660254AAB63FFBA8AFDD7C7848D0E050D4A59730E9B15082B4A2D11FA669D8BB03347BA4003E609513DFA2BA669C18F78924EFC6C22B3354BEE21E647E87857F5CEBA44D8BCE70F2FF49578C1368CE12041F277E7D3C606F1F5CA19E027B946FF9626325A0CDC6A9A005AA7192205E67524D4B9ADAC973C374063CA0AE1961866FD7A1CEAC7A212B5CB45D320E1A0BA7D2AD2E0FC9846A2CECA2DE64619A7B93F11704620D4B7F8FC84019F22321CCB46913515168049CC7D7978ECFB14CD9FB8AB40DD8DA4B3D07DBD4FABAD4FF36650F2E24B1691C80EA4A58141887E072531520D4790FDA78B682F63CBB568A50802824EC68AD6E2C11051162908693D7E"),
        new(
            7,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.GetMirrorModeCommand,
            0x013E,
            "000701E0109F312D74695620AE06BC563281D82FE3DD7C68D1"),
        new(
            8,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            0x013C,
            "000701E01049D860FA79896FC2B63AEB90606A31CB3A3526A1"),
        new(
            9,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            0x013F,
            "000701E008E9043CA080FF8BE45F1CD50F1C32880412D70885D666F203C78E71D4F7B16D4B890107AE"),
        new(
            10,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            0x0140,
            "000701E007856BF18508E96FD3C7480B71B7FB453961655E87DAD91A140A9FF8F49FCCE0DA715A0356"),
        new(
            11,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            0x0141,
            "000701E00893B7B56D060BB171DF93D413E5F78160766E307909931C1C866965768321DDB8690741EE"),
        new(
            12,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            0x0142,
            "000701E00726126CFB32F2A92BD2F263AE322B62951392212EC3B65E852C5065CA6B8AA80AA74C4ED4"),
        new(
            13,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            0x013D,
            "000701E010752B20440C81B25D3A6DE21EA46B3D919A3A5CD7"),
        new(
            14,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            0x013E,
            "000701E00BB2D1A8400E8DA649CB04F065C1795B7737F66330"),
        new(
            15,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            0x013F,
            "000701E0108080047BF0D0F5C8C6D4661F2F39961C2DE05111"),
        new(
            16,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            0x0140,
            "000701E010115A6CF5C3ECCF422A0F37150357A3A176FA1566"),
        new(
            17,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            0x0141,
            "000701E0107612D6D18D5464541B8AAC4C071421F4631DB8E0"),
        new(
            18,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            0x0142,
            "000701E01053B13EB6426FA70F3D4AF0A5F549D2FDF26E77DF"),
        new(
            19,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.HeartbeatCommand,
            0x0143,
            "000701E01044C848F68DB50C0654ECB64DB293F7C1C126272A"),
        new(
            20,
            MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint,
            MiPlayProtocolConstants.HeartbeatAcknowledgementCommand,
            0x0143,
            "000701E0106B92DA3D4BF5F2DB6B26ECC782E3B3A08023E91E"),
        new(
            21,
            MiPlayRealPhonePostAuthPlaintextEvidence.PhoneEndpoint + "->" + MiPlayRealPhonePostAuthPlaintextEvidence.SpeakerEndpoint,
            MiPlayProtocolConstants.SetPlaySourceCommand,
            0x0144,
            "000701E00B27C1E649DD46135AABE193489FEB9CF8D23A06AB4B43205A80A7CA27149BC22F7E1B62E98D8CA6C4B17131246926E7C5CEFC71FBE857F14549B9E9687A559F64FC2BEA4680568CD663E8971CD7342C544F730DB4EFCB843B67A4BE2BC920727DB6349843"),
    ];

    private sealed record CapturedSafetyDataFrame(
        int Index,
        string Direction,
        ushort Command,
        ushort Sequence,
        string SafetyDataPayloadHex);
}
