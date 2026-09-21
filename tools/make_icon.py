from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ICON_SIZES = [
    (16, 16),
    (20, 20),
    (24, 24),
    (32, 32),
    (40, 40),
    (48, 48),
    (64, 64),
    (128, 128),
    (256, 256),
]


def remove_connected_background(source: Image.Image) -> Image.Image:
    rgb = source.convert("RGB")
    marked = rgb.copy()
    width, height = marked.size
    marker = (1, 254, 1)

    for seed in ((0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1)):
        ImageDraw.floodfill(marked, seed, marker, thresh=32)

    pixels = np.asarray(marked)
    background = (
        (pixels[:, :, 0] < 10)
        & (pixels[:, :, 1] > 245)
        & (pixels[:, :, 2] < 10)
    )
    alpha = np.where(background, 0, 255).astype(np.uint8)

    result = rgb.convert("RGBA")
    result.putalpha(Image.fromarray(alpha, "L"))
    return result


def add_padding(image: Image.Image, ratio: float = 0.055) -> Image.Image:
    longest_side = max(image.size)
    padding = max(4, int(longest_side * ratio))
    canvas = Image.new(
        "RGBA",
        (image.width + padding * 2, image.height + padding * 2),
        (0, 0, 0, 0),
    )
    canvas.alpha_composite(image, (padding, padding))
    return canvas


def make_preview(icon: Image.Image, destination: Path) -> None:
    preview = Image.new("RGBA", (512, 512), (36, 39, 46, 255))
    draw = ImageDraw.Draw(preview)
    tile_size = 32

    for y in range(0, preview.height, tile_size):
        for x in range(0, preview.width, tile_size):
            if (x // tile_size + y // tile_size) % 2 == 0:
                draw.rectangle(
                    (x, y, x + tile_size - 1, y + tile_size - 1),
                    fill=(58, 62, 72, 255),
                )

    preview.alpha_composite(
        icon.resize((460, 460), Image.Resampling.LANCZOS),
        (26, 26),
    )
    preview.save(destination)


def main() -> None:
    parser = argparse.ArgumentParser(description="Build the Windows application icon.")
    parser.add_argument("source", type=Path)
    parser.add_argument("--output", type=Path, default=Path("assets/CcdAffinityManager.ico"))
    parser.add_argument("--preview", type=Path, default=Path("assets/icon-preview.png"))
    args = parser.parse_args()

    source = Image.open(args.source)
    transparent = remove_connected_background(source)
    bounds = transparent.getchannel("A").getbbox()
    if bounds is None:
        raise ValueError("The source image became fully transparent.")

    icon = add_padding(transparent.crop(bounds))
    icon.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
    icon.resize((256, 256), Image.Resampling.LANCZOS).save(
        args.output,
        format="ICO",
        sizes=ICON_SIZES,
    )
    make_preview(icon, args.preview)
    print(f"Created {args.output} and {args.preview}")


if __name__ == "__main__":
    main()
