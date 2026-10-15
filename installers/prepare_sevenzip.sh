#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
output="$1"
cache="$root/.tools/sevenzip-26.04-linux"
mkdir -p "$cache" "$output"
archive="$cache/7z2604-linux-x64.tar.xz"
expected="fc0327ba27e89bd086cf426dff17d77de582953cdbbc10a6576540a06853ffcd"
if [[ ! -f "$archive" ]] || ! echo "$expected  $archive" | sha256sum --check --status; then
  curl --fail --location --retry 3 'https://github.com/ip7z/7zip/releases/download/26.04/7z2604-linux-x64.tar.xz' --output "$archive"
fi
echo "$expected  $archive" | sha256sum --check --status
tar -xJf "$archive" -C "$cache" 7zz
cp "$cache/7zz" "$output/LunaArc.7zz"
chmod +x "$output/LunaArc.7zz"
cp "$root"/branding/SevenZip_{LICENSE,COPYING,NOTICE}.txt "$output/"
