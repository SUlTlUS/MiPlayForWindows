using System.Net;
using System.Net.Sockets;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Owns one session's reverse TCP and timer UDP endpoints. Port zero delegates
/// collision-free allocation to Windows, allowing multiple MiPlay sessions on
/// the same source address without sharing listeners.
/// </summary>
internal sealed class MiPlayReverseEndpointLease : IDisposable
{
    public MiPlayReverseEndpointLease(IPAddress sourceAddress)
    {
        ArgumentNullException.ThrowIfNull(sourceAddress);
        if (sourceAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new NotSupportedException("MiPlay reverse endpoints require IPv4.");
        }

        ReverseListener = new TcpListener(sourceAddress, 0);
        try
        {
            ReverseListener.Start(backlog: 3);
            Timer = new UdpClient(new IPEndPoint(sourceAddress, 0));
        }
        catch
        {
            ReverseListener.Stop();
            throw;
        }

        ReverseTcpPort = ((IPEndPoint)ReverseListener.LocalEndpoint).Port;
        TimerUdpPort = ((IPEndPoint)Timer.Client.LocalEndPoint!).Port;
    }

    public TcpListener ReverseListener { get; }
    public UdpClient Timer { get; }
    public int ReverseTcpPort { get; }
    public int TimerUdpPort { get; }

    public void Dispose()
    {
        Timer.Dispose();
        ReverseListener.Stop();
    }
}
