using System.Runtime.InteropServices;

namespace DLNACast.Core.Platform;

public sealed record NetworkProfileStatus(bool IsPrivate, string Summary);

public sealed class NetworkProfileService
{
    private static readonly Guid NetworkListManagerClassId = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    public NetworkProfileStatus GetStatus()
    {
        object? managerObject = null;
        try
        {
            var managerType = Type.GetTypeFromCLSID(NetworkListManagerClassId, throwOnError: true)!;
            managerObject = Activator.CreateInstance(managerType);
            dynamic manager = managerObject!;
            dynamic networks = manager.GetNetworks(1); // NLM_ENUM_NETWORK_CONNECTED
            var connected = new List<(string Name, int Category)>();
            foreach (dynamic network in networks)
            {
                try
                {
                    connected.Add(((string)network.GetName(), (int)network.GetCategory()));
                }
                finally
                {
                    if (Marshal.IsComObject(network)) Marshal.FinalReleaseComObject(network);
                }
            }

            var privateNetwork = connected.FirstOrDefault(item => item.Category is 1 or 2);
            if (!string.IsNullOrWhiteSpace(privateNetwork.Name))
            {
                return new NetworkProfileStatus(true, $"专用网络：{privateNetwork.Name}");
            }

            var names = string.Join("、", connected.Select(item => item.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
            return new NetworkProfileStatus(false, string.IsNullOrWhiteSpace(names)
                ? "没有检测到已连接的专用网络"
                : $"当前网络为公用：{names}");
        }
        catch (Exception ex)
        {
            return new NetworkProfileStatus(false, $"无法确认专用网络：{ex.Message}");
        }
        finally
        {
            if (managerObject is not null && Marshal.IsComObject(managerObject))
            {
                Marshal.FinalReleaseComObject(managerObject);
            }
        }
    }
}

