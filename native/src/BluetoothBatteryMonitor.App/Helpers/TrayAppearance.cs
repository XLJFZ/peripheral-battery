using System.Drawing;
using System.Text.RegularExpressions;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Helpers;

public static class TrayAppearance
{
    public static string ValidColor(string? value,string fallback) =>
        value!=null && Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$")?value:fallback;
    public static Color GetColor(int? level,AppSettings settings)
    {
        if(!level.HasValue)return Color.FromArgb(155,160,172);
        string color=!settings.TrayAutoColor?ValidColor(settings.TrayFixedColor,"#61B8FF"):
            level<10?ValidColor(settings.TrayLowColor,"#FF6577"):
            level<=30?ValidColor(settings.TrayMidColor,"#FFC048"):ValidColor(settings.TrayHighColor,"#00C98D");
        return ColorTranslator.FromHtml(color);
    }
}
