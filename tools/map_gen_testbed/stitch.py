"""Compose a grid of tiles into a single mosaic PNG."""

from __future__ import annotations

from typing import Optional

from PIL import Image


def stitch(grid: list[list[Optional[Image.Image]]], tile_px: int = 512) -> Image.Image:
    """grid[y][x] — row-major, y=0 is top row."""
    h = len(grid)
    w = len(grid[0]) if h else 0
    mosaic = Image.new("RGB", (w * tile_px, h * tile_px), (24, 24, 24))
    for y in range(h):
        for x in range(w):
            t = grid[y][x]
            if t is None:
                continue
            if t.size != (tile_px, tile_px):
                t = t.resize((tile_px, tile_px), Image.LANCZOS)
            mosaic.paste(t, (x * tile_px, y * tile_px))
    return mosaic
