"""Regenerate the packed industrial pipe sprites from their layer PNGs with Pillow."""

import json
from math import ceil, sqrt
from pathlib import Path

from PIL import Image, PngImagePlugin


PIPING = Path(__file__).resolve().parents[1] / "Resources/Textures/_Eclipse/Structures/Piping"
FRAME_SIZE = 32


def load_layer(folder, name):
    path = PIPING / folder / f"{name}.png"
    with Image.open(path) as source:
        if source.size != (FRAME_SIZE, FRAME_SIZE):
            raise ValueError(f"{path} must be {FRAME_SIZE}x{FRAME_SIZE}")
        return source.convert("RGBA")


def pack(parts, name, prefix, include_content=False):
    directions = [load_layer(parts, side) for side in ("north", "south", "west", "east")]
    hub = load_layer(parts, "hub")
    states = {
        f"{prefix}{mask}": [layer for bit, layer in enumerate(directions) if mask & (1 << bit)] + [hub]
        for mask in range(16)
    }
    if include_content:
        states["content"] = [load_layer(parts, "content")]
    states["connector"] = [load_layer("connector_parts", "socket")]
    states["flow_arrow"] = [load_layer("connector_parts", "arrow")]

    columns = ceil(sqrt(len(states)))
    rows = ceil(len(states) / columns)
    atlas = Image.new("RGBA", (columns * FRAME_SIZE, rows * FRAME_SIZE))
    for index, layers in enumerate(states.values()):
        origin = (index % columns * FRAME_SIZE, index // columns * FRAME_SIZE)
        for layer in layers:
            atlas.alpha_composite(layer, origin)

    metadata = {
        "version": 1,
        "size": {"x": FRAME_SIZE, "y": FRAME_SIZE},
        "states": [{"name": state} for state in states],
    }
    png_info = PngImagePlugin.PngInfo()
    png_info.add_text("robusttoolbox_rsic_meta", json.dumps(metadata, separators=(",", ":")))
    atlas.save(PIPING / f"{name}.rsic", format="PNG", pnginfo=png_info)


if __name__ == "__main__":
    pack("item_pipe_parts", "item_lp_pipe", "lp")
    pack("liquid_pipe_parts", "liquid_pipe", "pipe", include_content=True)
