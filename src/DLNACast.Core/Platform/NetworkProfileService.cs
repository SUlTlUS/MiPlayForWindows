using System.Runtime.InteropServices;
using DLNACast.Core.Localization;

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

            var (Name, Category) = connected.FirstOrDefault(item => item.Category is 1 or 2);
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return new NetworkProfileStatus(true, SystemLanguage.Select(
                    $"专用网络：{Name}",
                    $"Private network: {Name}"));
            }

            var names = string.Join(
                SystemLanguage.Select("、", ", "),
                connected.Select(item => item.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
            return new NetworkProfileStatus(false, string.IsNullOrWhiteSpace(names)
                ? SystemLanguage.Select("没有检测到已连接的专用网络", "No connected Private network was detected")
                : SystemLanguage.Select($"当前网络为公用：{names}", $"Current network is Public: {names}"));
        }
        catch (Exception ex)
        {
            return new NetworkProfileStatus(false, SystemLanguage.Select(
                $"无法确认专用网络：{ex.Message}",
                $"Unable to verify the Private network: {ex.Message}"));
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
