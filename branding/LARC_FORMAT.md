# LARC formats and compatibility

LunaArc 2.2 reads LARC v1 and v2. New archives without a password retain v1.
New password-protected archives use v2; LunaArc 2.1 and older cannot open them.
Existing encrypted v1 archives remain readable and are not rewritten automatically.

## V1

Magic `LUNAARC` plus byte `01`, one flags byte, a little-endian signed 32-bit
compressed metadata length, compressed JSON metadata, then indexed payloads.
The legacy password option uses per-header/per-payload PBKDF2-SHA256 and AES-CBC.
It has no cryptographic authentication. CRC32 detects ordinary corruption.

## V2 authenticated envelope

The envelope encrypts an entire **unencrypted v1 archive**, including names and
comments. It does not silently modify v1 flags or payload layouts.

| Offset | Bytes | Meaning |
| --- | --- | --- |
| 0 | 8 | ASCII `LUNAARC` followed by byte `02` |
| 8 | 16 | Random PBKDF2 salt |
| 24 | 8 | Random nonce prefix |
| 32 | 4 | Little-endian chunk size, exactly 1,048,576 |
| 36 | 4 | Little-endian KDF iteration count, exactly 600,000 |
| 40 | 8 | Little-endian signed plaintext archive length, 13 bytes to 1 TiB |

PBKDF2-SHA256 derives a 32-byte key from the UTF-8 password and salt. Each record
uses AES-256-GCM with a 16-byte tag. Its nonce is the eight-byte prefix followed
by a **big-endian unsigned 32-bit record index**, starting at zero. Associated
data consists of the complete 48-byte header, a **little-endian unsigned 32-bit
index**, and the **little-endian signed 32-bit plaintext record length**.

Records have no separate length fields: the authenticated header determines
full 1 MiB chunks and the final partial chunk. Each ciphertext is followed by
its tag. A mandatory **empty ciphertext plus tag** follows the last data record,
using the next index and length zero. It authenticates completion. The reader
rejects missing/extra bytes before deriving the key, and rejects any failed tag.
Nonce indexes cannot wrap under the 1 TiB format limit. Writers generate a new
random salt and prefix for every archive and clear derived keys after use.

The reader authenticates/decrypts into a session temporary archive before
catalog parsing or extraction into user destinations. No partially authenticated
contents are published to the destination. Temporary plaintext is removed on
normal completion, failure or cancellation; secure erasure or recovery from an
abrupt process/OS termination is not promised. Opening/testing/extracting v2
needs a full authentication pass and temporary disk space for the inner archive.
This is an application format with regression tests, not an external security audit.

## LunaArc split sets

The raw bytes of a completed, verified archive are split into `archive.ext.001`,
`.002`, and subsequent numbered files. Keep `archive.ext.lunasplit.json` beside
them. The bounded JSON manifest records version 1, the archive name, total size
and ordered part names, lengths and SHA-256 hashes. Limits: 10,000 parts,
minimum creation volume size 64 KiB, maximum manifest size 4 MiB.

Open `.001` to assemble and verify into a temporary archive before normal
decoding. Missing parts, wrong lengths, checksum failures and unsafe paths fail
before extraction. SHA-256 records detect damage; they are not a signature.
Password-protected LARC v2 still verifies GCM tags even if somebody rewrites the
split manifest. These are LunaArc split sets, not RAR volumes or native split ZIP
disks. Legacy `.part001.ext` files without a manifest are not imported.
