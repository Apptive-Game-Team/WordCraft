#!/usr/bin/env bash
# Regenerate art contact sheets.
#   .art/sheets/master-v2-anchors.png - canonical style anchors
#   .art/sheets/sprites.png  - every live magic sprite (drift review)
#   .art/sheets/concept.png  - concept art variants, if any exist (style selection)
set -euo pipefail

cd "$(dirname "$0")/.."

FONT=/System/Library/Fonts/Helvetica.ttc
mkdir -p .art/sheets

magick montage -font "$FONT" -pointsize 12 -fill white \
  .art/anchors/master-v2/*.png \
  -background '#303030' -tile 5x -geometry 280x280+8+8 -label '%t' \
  .art/sheets/master-v2-anchors.png

magick montage -font "$FONT" -pointsize 10 -fill white \
  Client/Assets/Resources/Art/Sprites/*.png \
  -background '#303030' -tile 8x -geometry 128x128+6+6 -label '%t' \
  .art/sheets/sprites.png

if compgen -G '.art/concept/*.png' > /dev/null; then
  magick montage -font "$FONT" -pointsize 14 -fill white \
    .art/concept/*.png \
    -background '#303030' -tile 3x -geometry 320x320+10+10 -label '%t' \
    .art/sheets/concept.png
fi

magick identify .art/sheets/*.png
