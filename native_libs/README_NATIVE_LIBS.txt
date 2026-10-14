Optional native libraries

Windows Explorer extension

WindowsContextMenu contains LunaArc's native C++ IExplorerCommand implementation,
CMake contract checks and sparse package manifest. Windows publishing builds this
DLL independently from the managed archive backend. See README.md for optional
Windows 11 registration and explicit certificate-trust requirements.

LunaArc currently uses managed NuGet dependencies (SharpCompress and ZstdSharp.Port).
No external 7-Zip DLL or shared library is required by the implemented archive backend.

Adding 7z.dll or 7z.so to this directory does not enable a native backend automatically.
A future adapter would need explicit loading code, per-platform packaging, compatibility
tests and applicable third-party license notices. Do not ship unverified native binaries.

Avalonia's own native rendering libraries are supplied through its NuGet packages and
included by the publish scripts. Linux still requires the host's graphical system libraries.
