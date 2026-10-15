#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
runtime="${1:-linux-x64}"
dotnet_cmd="${DOTNET_COMMAND:-dotnet}"
if ! command -v "$dotnet_cmd" >/dev/null && [[ -x "$project_root/.tools/dotnet/dotnet" ]]; then
  dotnet_cmd="$project_root/.tools/dotnet/dotnet"
fi
case "$runtime" in
  linux-x64|win-x64) ;;
  *) echo "Unsupported runtime: $runtime. Use linux-x64 or win-x64." >&2; exit 2 ;;
esac
command -v "$dotnet_cmd" >/dev/null || { echo "Install the .NET 8 SDK or set DOTNET_COMMAND." >&2; exit 1; }
cd "$project_root"
publish_dir="$project_root/dist/$runtime"
echo "Publishing LunaArc for $runtime..."
for project in LunaArc.Cli LunaArc.Studio; do
  "$dotnet_cmd" publish "src/$project/$project.csproj" -c Release -r "$runtime" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o "$publish_dir"
done
suffix=""
if [[ "$runtime" == win-* ]]; then suffix=".exe"; fi
for name in LunaArc luna-cli; do
  test -f "$publish_dir/$name$suffix"
done
if [[ "$runtime" == linux-* ]]; then chmod +x "$publish_dir/LunaArc" "$publish_dir/luna-cli"; fi
cp "$project_root/branding/EULA_License.txt" "$project_root/README.md" "$publish_dir/"
cp "$project_root/branding/Fluent_Icons_LICENSE.txt" "$publish_dir/"
cp "$project_root/branding/app_logo_256.png" "$publish_dir/LunaArc.png"
if [[ "$runtime" == linux-* ]]; then
  bash "$project_root/installers/prepare_sevenzip.sh" "$publish_dir"
else
  pwsh -NoProfile -File "$project_root/installers/prepare_sevenzip.ps1" -OutputDirectory "$publish_dir"
fi
if [[ "$runtime" == win-* ]]; then
  cp "$project_root/installers/InstallContextMenu.cmd" "$project_root/installers/UninstallContextMenu.cmd" "$publish_dir/"
fi
echo "Publish succeeded: $publish_dir"
