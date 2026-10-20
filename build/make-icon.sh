#!/usr/bin/env sh
# Пересобрать значок программы из SVG:
#   docs/assets/icon-small.svg → 16, 20, 24 px; docs/assets/icon.svg → 32…256 px
#   → src/DropWheel/Assets/DropWheel.ico (все размеры в одном файле).
# Нужны: rsvg-convert (librsvg) или python3 с пакетом cairosvg; ImageMagick (convert, identify).
set -e
root=$(cd "$(dirname "$0")/.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

render() { # svg size out.png
  if command -v rsvg-convert >/dev/null 2>&1; then
    rsvg-convert -w "$2" -h "$2" "$1" -o "$3"
  else
    python3 -c "import cairosvg, sys; cairosvg.svg2png(url=sys.argv[1], write_to=sys.argv[3], output_width=int(sys.argv[2]), output_height=int(sys.argv[2]))" "$1" "$2" "$3"
  fi
}

pngs=""
for s in 16 20 24; do render "$root/docs/assets/icon-small.svg" "$s" "$tmp/$s.png"; pngs="$pngs $tmp/$s.png"; done
for s in 32 40 48 64 128 256; do render "$root/docs/assets/icon.svg" "$s" "$tmp/$s.png"; pngs="$pngs $tmp/$s.png"; done
# shellcheck disable=SC2086
convert $pngs "$root/src/DropWheel/Assets/DropWheel.ico"
identify "$root/src/DropWheel/Assets/DropWheel.ico"
