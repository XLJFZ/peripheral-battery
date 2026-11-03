using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

/// <summary>
/// Resolves the Razer device catalogue from next to the executable, or from the copy
/// embedded in the assembly so the single-file portable build stays self-contained.
/// </summary>
public static class RazerCatalog
{
    private const string FileName = "razer_devices.json";

    private static string ExternalPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static bool Exists => File.Exists(ExternalPath) || OpenEmbedded() != null;

    public static async Task<string> ReadAllTextAsync(CancellationToken ct = default)
    {
        if (File.Exists(ExternalPath))
        {
            return await File.ReadAllTextAsync(ExternalPath, ct);
        }

        using var stream = OpenEmbedded()
            ?? throw new FileNotFoundException("razer_devices.json 缺失（外部文件与内置副本均不可用）");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private static Stream? OpenEmbedded()
        => typeof(RazerCatalog).Assembly.GetManifestResourceStream(FileName);
}
