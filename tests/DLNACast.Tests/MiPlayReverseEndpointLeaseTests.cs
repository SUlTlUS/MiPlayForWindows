using System.Net;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayReverseEndpointLeaseTests
{
    [Fact]
    public void TwoSessionsOnOneAddressReceiveIndependentReverseEndpoints()
    {
        using var first = new MiPlayReverseEndpointLease(IPAddress.Loopback);
        using var second = new MiPlayReverseEndpointLease(IPAddress.Loopback);

        Assert.InRange(first.ReverseTcpPort, IPEndPoint.MinPort, IPEndPoint.MaxPort);
        Assert.InRange(first.TimerUdpPort, IPEndPoint.MinPort, IPEndPoint.MaxPort);
        Assert.NotEqual(first.ReverseTcpPort, second.ReverseTcpPort);
        Assert.NotEqual(first.TimerUdpPort, second.TimerUdpPort);
        Assert.True(first.ReverseListener.Server.IsBound);
        Assert.True(second.ReverseListener.Server.IsBound);
    }
}
