using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public class BleGattBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "BLE GATT (0x180F)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    private readonly ConcurrentDictionary<string, BluetoothLEDevice> _activeDevices = new();
    private readonly ConcurrentDictionary<string, GattCharacteristic> _subscribedCharacteristics = new();
    private bool _isMonitoring;

    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var resultList = new List<BluetoothDeviceModel>();

        try
        {
            // Scan paired BLE devices
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var deviceInfos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);

            foreach (var info in deviceInfos)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var bleDevice = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
                    if (bleDevice == null) continue;

                    // keepAlive = a live notification subscription now owns this instance and it is
                    // registered in _activeDevices; the caller must not dispose it.
                    bool keepAlive = false;
                    try
                    {
                        // Query Battery Service (0x180F)
                        var servicesResult = await bleDevice.GetGattServicesForUuidAsync(
                            GattServiceUuids.Battery,
                            BluetoothCacheMode.Uncached).AsTask(cancellationToken);

                        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                        {
                            // Retry from cache
                            servicesResult = await bleDevice.GetGattServicesForUuidAsync(
                                GattServiceUuids.Battery,
                                BluetoothCacheMode.Cached).AsTask(cancellationToken);
                        }

                        if (servicesResult.Status == GattCommunicationStatus.Success && servicesResult.Services.Count > 0)
                        {
                            foreach (var service in servicesResult.Services)
                            {
                                var charResult = await service.GetCharacteristicsForUuidAsync(
                                    GattCharacteristicUuids.BatteryLevel,
                                    BluetoothCacheMode.Uncached).AsTask(cancellationToken);

                                if (charResult.Status == GattCommunicationStatus.Success && charResult.Characteristics.Count > 0)
                                {
                                    var batteryChar = charResult.Characteristics[0];
                                    var readResult = await batteryChar.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);

                                    if (readResult.Status == GattCommunicationStatus.Success && readResult.Value != null)
                                    {
                                        byte[] data = readResult.Value.ToArray();
                                        if (data.Length > 0)
                                        {
                                            int level = data[0];
                                            var model = new BluetoothDeviceModel
                                            {
                                                Id = info.Id,
                                                Name = !string.IsNullOrWhiteSpace(bleDevice.Name) ? bleDevice.Name : info.Name,
                                                BatteryLevel = level,
                                                IsConnected = bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected,
                                                DeviceType = DetectDeviceType(info.Name),
                                                BluetoothAddress = bleDevice.BluetoothAddress != 0 ? bleDevice.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(info.Id),
                                                ProviderSource = "BLE GATT",
                                                LastUpdated = DateTime.Now
                                            };

                                            resultList.Add(model);

                                            // Subscribe to characteristic notifications if monitoring is active.
                                            // Awaited (not fire-and-forget): the keepAlive decision must be
                                            // settled before this scope disposes the instance.
                                            if (_isMonitoring)
                                            {
                                                keepAlive = await SubscribeToBatteryNotificationAsync(info.Id, batteryChar, bleDevice);
                                            }

                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (!keepAlive)
                        {
                            bleDevice.Dispose();
                        }
                    }
                }
                catch
                {
                    // Continue to next device if communication fails
                }
            }
        }
        catch
        {
            // General scan error
        }

        return resultList;
    }

    public void StartMonitoring()
    {
        _isMonitoring = true;
    }

    public void StopMonitoring()
    {
        _isMonitoring = false;
        foreach (var pair in _subscribedCharacteristics)
        {
            try
            {
                pair.Value.ValueChanged -= OnCharacteristicValueChanged;
            }
            catch { }
        }
        _subscribedCharacteristics.Clear();

        foreach (var pair in _activeDevices)
        {
            try
            {
                pair.Value.Dispose();
            }
            catch { }
        }
        _activeDevices.Clear();
    }

    /// <summary>
    /// Subscribes to battery notifications on <paramref name="batteryChar"/>.
    /// Returns true when a subscription is now live and <paramref name="bleDevice"/> has been
    /// registered in _activeDevices — the parent BluetoothLEDevice must stay alive for the
    /// lifetime of the subscription, otherwise the OS tears the session down and notifications
    /// never fire (the pre-0.3.15 behaviour, where each scan disposed the instance it had just
    /// subscribed on). Returns false when the caller should dispose the instance.
    /// </summary>
    private async Task<bool> SubscribeToBatteryNotificationAsync(string deviceId, GattCharacteristic batteryChar, BluetoothLEDevice bleDevice)
    {
        if (_subscribedCharacteristics.ContainsKey(deviceId)) return false;

        try
        {
            if (batteryChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) ||
                batteryChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
            {
                batteryChar.ValueChanged += OnCharacteristicValueChanged;
                var status = await batteryChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if (status == GattCommunicationStatus.Success)
                {
                    _subscribedCharacteristics[deviceId] = batteryChar;

                    if (_activeDevices.TryGetValue(deviceId, out var existing) && !ReferenceEquals(existing, bleDevice))
                    {
                        _activeDevices[deviceId] = bleDevice;
                        try { existing.Dispose(); } catch { }
                    }
                    else
                    {
                        _activeDevices[deviceId] = bleDevice;
                    }

                    return true;
                }

                batteryChar.ValueChanged -= OnCharacteristicValueChanged;
            }
        }
        catch
        {
            // Subscription may not be supported
        }

        return false;
    }

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            byte[] data = args.CharacteristicValue.ToArray();
            if (data.Length > 0)
            {
                int level = data[0];
                var device = sender.Service.Device;
                var model = new BluetoothDeviceModel
                {
                    Id = device.DeviceId,
                    Name = !string.IsNullOrWhiteSpace(device.Name) ? device.Name : "BLE Device",
                    BatteryLevel = level,
                    IsConnected = device.ConnectionStatus == BluetoothConnectionStatus.Connected,
                    DeviceType = DetectDeviceType(device.Name),
                    BluetoothAddress = device.BluetoothAddress != 0 ? device.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(device.DeviceId),
                    ProviderSource = "BLE GATT (Live)",
                    LastUpdated = DateTime.Now
                };

                DeviceUpdated?.Invoke(this, model);
            }
        }
        catch
        {
            // Event processing error
        }
    }

    private static DeviceType DetectDeviceType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return DeviceType.Generic;
        string lower = name.ToLowerInvariant();

        if (lower.Contains("airpod") || lower.Contains("earbud") || lower.Contains("tws") || lower.Contains("buds") || lower.Contains("freebuds"))
            return DeviceType.Earbuds;
        if (lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("wh-") || lower.Contains("kulaklık") || lower.Contains("soundcore"))
            return DeviceType.Headphones;
        if (lower.Contains("speaker") || lower.Contains("hoparlör") || lower.Contains("soundbar") || lower.Contains("flip") || lower.Contains("charge"))
            return DeviceType.Speaker;
        if (lower.Contains("mouse") || lower.Contains("fare") || lower.Contains("trackball") || lower.Contains("mx master") || lower.Contains("anywhere"))
            return DeviceType.Mouse;
        if (lower.Contains("keyboard") || lower.Contains("klavye") || lower.Contains("keychron") || lower.Contains("k380"))
            return DeviceType.Keyboard;
        if (lower.Contains("controller") || lower.Contains("xbox") || lower.Contains("dualshock") || lower.Contains("dualsense") || lower.Contains("gamepad") || lower.Contains("joy-con"))
            return DeviceType.Gamepad;
        if (lower.Contains("iphone") || lower.Contains("phone") || lower.Contains("telefon") || lower.Contains("galaxy s") || lower.Contains("pixel") || lower.Contains("xperia") || lower.Contains("xiaomi") || lower.Contains("huawei") || lower.Contains("oneplus"))
            return DeviceType.Phone;
        if (lower.Contains("watch") || lower.Contains("saat") || lower.Contains("band") || lower.Contains("fit"))
            return DeviceType.Watch;
        if (lower.Contains("pen") || lower.Contains("stylus") || lower.Contains("pencil"))
            return DeviceType.Stylus;

        return DeviceType.Generic;
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
