# Third-party components

DialShift uses unmodified, dynamically linked dependencies:

- **LibVLCSharp 3.10.1** — LGPL-2.1-or-later. [Project and source](https://code.videolan.org/videolan/LibVLCSharp), [NuGet](https://www.nuget.org/packages/LibVLCSharp/3.10.1).
- **VideoLAN.LibVLC.Windows 3.0.23.1** — VLC/LibVLC native runtime and plugins. LibVLC is LGPL-2.1-or-later; bundled plugins and their dependencies have individual licenses, including GPL components. [Packaging/source instructions](https://code.videolan.org/videolan/libvlc-nuget), [VLC source](https://code.videolan.org/videolan/vlc/-/tree/3.0.23), [VideoLAN legal information](https://www.videolan.org/legal.html).
- **.NET 10 / WPF / Windows Forms** — Microsoft and contributors, MIT and component notices. [Runtime](https://github.com/dotnet/runtime), [WPF](https://github.com/dotnet/wpf), [Windows Forms](https://github.com/dotnet/winforms). The self-contained release carries runtime license/notice files.

Native VLC libraries remain separate in `libvlc/win-x64` so compatible replacements can be supplied. No changes to these third-party libraries were made. Station names, broadcasts and programming belong to the respective providers. The included links are for personal listening.
