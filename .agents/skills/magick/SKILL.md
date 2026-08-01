---
name: magick
description: ImageMagick CLI image inspection, conversion, resizing, cropping, optimization, compositing, metadata handling, and batch processing. Use when Codex needs to manipulate raster images with the `magick` command, verify dimensions or formats, prepare web/app/game image assets, make thumbnails, strip metadata, compose simple image outputs, or convert between PNG, JPEG, WebP, GIF, TIFF, HEIC, and related formats.
---

# Magick

## Workflow

Prefer the ImageMagick 7 `magick` command. First check availability:

```bash
magick -version
```

For image inspection, use `identify` through `magick` and capture enough information to avoid guessing:

```bash
magick identify image.png
magick identify -verbose image.png
magick identify -format '%f %m %wx%h %[channels] %[colorspace]\n' image.png
```

Before editing user-provided originals, write outputs to new paths unless the user explicitly asks to overwrite. Preserve file extensions that match the output format.

## Common Tasks

Convert formats:

```bash
magick input.png output.webp
magick input.heic output.png
magick input.tif output.jpg
```

Resize while preserving aspect ratio:

```bash
magick input.png -resize 1024x1024 output.png
magick input.png -resize 50% output.png
```

Resize to exact dimensions with crop:

```bash
magick input.png -resize 512x512^ -gravity center -extent 512x512 output.png
```

Make a transparent-background crop from existing alpha:

```bash
magick input.png -trim +repage output.png
```

Strip metadata and normalize orientation for web output:

```bash
magick input.jpg -auto-orient -strip -quality 85 output.jpg
magick input.png -strip output.png
```

Create thumbnails:

```bash
magick input.png -thumbnail 256x256 thumbnail.png
```

Composite an overlay:

```bash
magick base.png overlay.png -gravity southeast -geometry +24+24 -composite output.png
```

Create a contact sheet:

```bash
magick montage *.png -thumbnail 160x160 -geometry 180x190+8+8 contact-sheet.png
```

## Batch Work

Use shell loops for straightforward batch operations. Quote paths and avoid overwriting originals:

```bash
for f in *.png; do
  magick "$f" -resize 1024x1024 "resized/${f%.png}.webp"
done
```

When file names may contain spaces or many formats are involved, use `find` with `-print0`:

```bash
find . -type f \( -name '*.png' -o -name '*.jpg' \) -print0 |
while IFS= read -r -d '' f; do
  out="optimized/${f#./}"
  mkdir -p "$(dirname "$out")"
  magick "$f" -auto-orient -strip -resize 1600x1600\> "$out"
done
```

## Validation

After generating files, verify outputs with `magick identify`:

```bash
magick identify -format '%f %m %wx%h %b\n' output.png
```

For visual assets in a repo, also inspect the generated image when precision matters. Use the environment's available image viewing or browser tooling rather than relying only on command success.

## Safety Notes

- Do not overwrite originals unless explicitly requested.
- Use `-auto-orient` for camera photos before resizing or cropping.
- Use `-strip` for web/app assets unless metadata must be preserved.
- Prefer PNG or WebP for transparency; JPEG does not preserve alpha.
- Prefer exact `-resize WxH^ -gravity center -extent WxH` for fixed-size icons, avatars, thumbnails, and game sprites.
- For large batches, test on one or two files first and show the command before scaling to the full set when destructive output paths are involved.
