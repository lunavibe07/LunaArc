# Password recovery fixtures

- `encrypted.zip` (ZipCrypto), `encrypted-aes.zip` (WinZip AES-256 AE-2), and `encrypted.7z` (encrypted headers) contain only `message.txt`: "Synthetic LunaArc recovery fixture." followed by CRLF. Password: `fixture3`.
- These three synthetic fixtures were generated with the official 7-Zip 25.01 standalone executable. Tests do not require 7-Zip to be installed.
- The four `Rar*.rar` fixtures are from [SharpCompress 0.50.5's test archives](https://github.com/adamhathcock/sharpcompress/tree/0.50.5/tests/TestArchives/Archives), used under its MIT license included as `SharpCompress-LICENSE.txt`. Password: `test`. They cover RAR/RAR5 with encrypted contents and encrypted headers. Tests read and verify their contents without launching any archived executable.

All passwords here are public synthetic test data. These fixtures are not shipped in the desktop publish folders.
