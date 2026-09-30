# UsbSpeed

A tiny, minimal Windows app that shows the speed each connected USB device is **actually running at**. Think of it as a stripped-down, flat-list alternative to USBView / USBTreeView.

It's handy for spotting a USB 3 drive that's silently running at USB 2 speed because of a bad port or cable.

<img width="803" height="366" alt="ZWWwQwflwg" src="https://github.com/user-attachments/assets/2f8b0b38-c63a-4681-9203-5cf5b9d1c495" />




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

## How it works

The app enumerates USB hubs through SetupAPI, then queries each port with the same `DeviceIoControl` calls used by Microsoft's USBView sample (`IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX` and its `_V2` variant) to read the connection speed and whether the device is capable of SuperSpeed or faster.

## Limitations

- Windows only
- Flat list, not a tree of hubs and devices
- Some devices don't expose a product name and show as "USB device"

## License

[MIT](LICENSE)
