# UsbSpeed

A tiny, minimal Windows app that shows the speed each connected USB device is **actually running at**. Think of it as a stripped-down, flat-list alternative to USBView / USBTreeView.

It's handy for spotting a USB 3 drive that's silently running at USB 2 speed because of a bad port or cable.

## Features

- Lists every connected USB device with its name, VID:PID and negotiated speed (Low / Full / High / SuperSpeed / SuperSpeed+)
- Warns (⚠) when a device supports a faster speed than it's currently using
- Refreshes automatically when you plug or unplug a device
- **Refresh** button (or **F5**) to re-scan manually
- Single small executable, no installer

## Download

Grab the latest `UsbSpeed.zip` from the [Releases](../../releases) page, extract it and run `UsbSpeed.exe`.

Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download).

> Windows SmartScreen may warn about an unsigned app the first time you run it. The full source is in this repo if you'd like to build it yourself.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/AkuraDev/usbspeed.git
cd UsbSpeed
dotnet run
```

To produce a single-file executable:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The result is in `bin\Release\net10.0-windows\win-x64\publish\`.

## How it works

The app enumerates USB hubs through SetupAPI, then queries each port with the same `DeviceIoControl` calls used by Microsoft's USBView sample (`IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX` and its `_V2` variant) to read the connection speed and whether the device is capable of SuperSpeed or faster.

## Limitations

- Windows only
- Flat list, not a tree of hubs and devices
- Some devices don't expose a product name and show as "USB device"

## License

[MIT](LICENSE)
