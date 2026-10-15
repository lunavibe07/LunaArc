Optional native libraries

Windows Explorer extension

WindowsContextMenu contains LunaArc's native C++ IExplorerCommand implementation,
CMake contract checks and sparse package manifest. Windows publishing builds this
DLL independently from the managed archive backend. See README.md for optional
Windows 11 registration and explicit certificate-trust requirements.

LunaArc uses managed NuGet dependencies for reading and extraction (SharpCompress
and ZstdSharp.Port). Creating standard 7z archives additionally uses the separate
unmodified 7-Zip 26.04 command-line encoder. Publishing downloads the appropriate
Windows 7zr.exe or Linux x64 7zz binary from the official release, validates a
pinned SHA-256, and includes it plus license/source notices in both editions.
No 7z.dll/7z.so adapter is loaded. See installers/prepare_sevenzip.* and
branding/SevenZip_NOTICE.txt. LUNAARC_7ZIP_PATH can select an alternative encoder.

Avalonia's own native rendering libraries are supplied through its NuGet packages and
included by the publish scripts. Linux still requires the host's graphical system libraries.
