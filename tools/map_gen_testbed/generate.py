"""End-to-end map generation pipeline (Python testbed).

Phases:
  1. Layout    — deterministic N×N grid with walls/floor/door/building.
  2. Style bible — 1 text LLM call; palette/materials/lighting/motifs.
  3. Spatial plan — 1 text LLM call; per-tile DESC/OBJECTS/EDGES/INTERIOR.
  4. Reconcile — deterministic edge alignment + boundary sentinels.
  5. Anchor pair — 2 image calls (Floor + Wall) to seed style vocabulary.
  6. BFS — sequential tile generation with neighbor textures as refs.
  7. Correction — multimodal pass flags up to N broken tiles, regenerates.

Outputs to out/<timestamp>/: tile_x_y.png, mosaic.png, style_bible.txt,
spatial_plan.json, run_log.txt.
"""

from __future__ import annotations

import argparse
import collections
import datetime
import json
import os
import re
import sys
from pathlib import Path
from typing import Optional

import yaml
from dotenv import load_dotenv
from PIL import Image

from openrouter_client import Client
from stitch import stitch


# ------------------------------------------------------------------ layout
FLOOR, WALL, DOOR, BUILDING = "Floor", "Wall", "Door", "Building"


def build_layout(size: int) -> list[list[str]]:
    """Wall border, floor interior, 4 doors on cardinal midpoints, 1 building."""
    g = [[FLOOR] * size for _ in range(size)]
    for i in range(size):
        g[0][i] = g[size - 1][i] = g[i][0] = g[i][size - 1] = WALL
    mid = size // 2
    g[0][mid] = g[size - 1][mid] = g[mid][0] = g[mid][size - 1] = DOOR
    if size >= 5:
        # one interior building, offset from center
        bx, by = size - 2, 1
        g[by][bx] = BUILDING
    return g


def layout_to_ascii(g: list[list[str]]) -> str:
    symbol = {FLOOR: ".", WALL: "#", DOOR: "+", BUILDING: "B"}
    lines = []
    for y, row in enumerate(g):
        cells = "".join(symbol[c] for c in row)
        lines.append(f"y={y}: {cells}")
    return "\n".join(lines)


def layout_to_prose(g: list[list[str]]) -> str:
    """Describe the layout in natural language so image models do not paint ASCII glyphs."""
    h = len(g)
    w = len(g[0]) if h else 0
    parts = ["A rectangular outer wall encloses the map; the interior is open ground."]
    door_descs = []
    for y, row in enumerate(g):
        for x, cell in enumerate(row):
            if cell != DOOR:
                continue
            on_top    = y == 0
            on_bottom = y == h - 1
            on_left   = x == 0
            on_right  = x == w - 1
            if on_top:    side = "top (north)"
            elif on_bottom: side = "bottom (south)"
            elif on_left:   side = "left (west)"
            elif on_right:  side = "right (east)"
            else:           side = "interior"
            # describe position along that side as a fraction
            if on_top or on_bottom:
                frac = x / max(1, w - 1)
            else:
                frac = y / max(1, h - 1)
            if frac < 0.34:   loc = "near the start"
            elif frac > 0.66: loc = "near the end"
            else:             loc = "centered"
            door_descs.append(f"{loc} of the {side} wall")
    if door_descs:
        parts.append("Open doorways (gaps in the wall, no doorframes drawn): " + "; ".join(door_descs) + ".")
    bld_descs = []
    for y, row in enumerate(g):
        for x, cell in enumerate(row):
            if cell != BUILDING:
                continue
            fx = x / max(1, w - 1)
            fy = y / max(1, h - 1)
            ns = "upper" if fy < 0.34 else ("lower" if fy > 0.66 else "middle")
            ew = "left" if fx < 0.34 else ("right" if fx > 0.66 else "center")
            bld_descs.append(f"{ns}-{ew}")
    if bld_descs:
        parts.append("Standalone building(s) sitting on the open floor: " + ", ".join(bld_descs) + ".")
    parts.append(f"Map proportions: {w} units wide by {h} units tall.")
    return " ".join(parts)


# ------------------------------------------------------------------ LLM prompts
def style_bible_prompt(theme: str) -> str:
    return f"""You are an art director establishing the visual bible for a pixel-art tile map.

THEME: {theme}

Return EXACTLY 5 lines, each starting with the label shown:
PALETTE: 3-5 specific colors with material context (e.g. "weather-bleached sandstone beige, moss-green highlights, rust-iron door hardware")
MATERIALS: surface treatments (e.g. "chipped flagstone, timber roof beams, tarnished brass")
LIGHTING: time of day and direction (e.g. "late afternoon, low warm sunlight from west, long soft shadows")
MOTIFS: 2-3 recurring details across all tiles (e.g. "ivy creeping on walls, candle stubs in every corner, fishing nets on posts")
EDGES: how adjacent tiles physically meet (e.g. "thick dark grout lines between cobbles, no painted borders around tile, flush seams")

No preamble. No bullet points. No extra lines. Just those 5 labeled lines."""


def spatial_plan_prompt(theme: str, size: int, layout: list[list[str]], bible: str) -> str:
    ascii_grid = layout_to_ascii(layout)
    return f"""You are planning the contents of a {size}x{size} orthographic overhead map for pixel-art rendering. The whole map is ONE CONTINUOUS SPACE viewed from directly above — imagine a sheet of paper drawn as one picture, then cut into a {size}x{size} grid of equal squares. Each square is what we render separately. They must recombine into the single picture with no seams and no added frames.

THEME: {theme}
STYLE BIBLE:
{bible}

LAYOUT (y=0 is top row):
{ascii_grid}

LAYOUT SYMBOL MEANINGS:
  #  Wall cell — a slab of exterior wall material (stone/timber/etc.). Walls form a CONTINUOUS ring around the map.
  +  Door cell — an OPENING in the wall ring. The tile's center has a door/gate threshold; the ground passes THROUGH it connecting the exterior to the interior floor.
  .  Floor cell — open interior ground (dirt, grass, cobble, etc.). Floor cells connect to each other with NO wall between them. The ground texture is continuous across floor↔floor seams.
  B  Building cell — exterior face of a standalone structure sitting on the floor. Building walls belong to that structure, not to the map border.

SEAM RULES (apply these when writing EDGE strings):
  wall↔wall (both # cells): the wall material continues across the seam — solid, unbroken.
  wall↔door (# next to + along the border): the wall meets the door frame/jamb; the wall ends and the door opening begins.
  wall↔floor (# next to . on interior): this is the INTERIOR FACE of the wall — the wall's base meets the floor; no objects cross it.
  floor↔floor (. next to .): the floor/ground texture is UNBROKEN across the seam. No wall, no frame, no border. Just ground. Dirt, grass, or cobbles continue as if the seam did not exist.
  door↔floor (+ next to .): the door threshold transitions into open floor — ground passes through.
  Anything↔map boundary (outermost edge of the grid): use "map boundary" — rendered as the natural termination of the material (ragged grass, weathered stone end, etc.).

For EACH tile output ONE line in EXACTLY this format:
TILE x,y: DESC=<rich sentence> | OBJECTS=<obj@pos, obj@pos, ...> | N=<what crosses the north edge> | E=<east edge> | S=<south edge> | W=<west edge> | INTERIOR=<only for Building tiles; theme+short blueprint, else empty>

RULES:
- Coordinates: x is column (0..{size-1}), y is row (0..{size-1}). y=0 is the TOP row.
- DESC: 1-2 concrete sentences. This is one SLICE of a larger continuous scene — never describe the tile as a standalone "scene", "picture", "view", or "area surrounded by walls". Just describe what physically sits in that square.
- OBJECTS: 0-3 items. Positions ∈ {{center, N, NE, E, SE, S, SW, W, NW, edge-N, edge-E, edge-S, edge-W}}. Use "none" for sparse tiles. Objects at "edge-*" must logically continue into the neighbor.
- EDGES: describe what OCCUPIES the seam between this tile and the neighbor on that side. EAST of tile x,y MUST match WEST of tile x+1,y (copy the same phrase). SOUTH of tile x,y MUST match NORTH of tile x,y+1. Outer-boundary edges say "map boundary". Apply the SEAM RULES above — do not invent walls between two floor tiles.
- INTERIOR: only for B tiles. Format "<theme>; <1-line blueprint>". Empty string for non-buildings.

Output ONLY the TILE lines, one per tile, separated by newlines. No JSON, no markdown, no preamble."""


def tile_refinement_prompt(
    bible: str,
    tile: dict,
    x: int,
    y: int,
    has_neighbor_refs: bool,
) -> str:
    edges = tile.get("edges", {})
    objects = tile.get("objects") or "none"
    composition = "" if objects.lower() == "none" else f"OBJECTS: {objects}\n"
    ref_note = (
        "The FIRST attached image is the existing rough version of THIS tile. "
        "The remaining attached images are the neighbor tiles already rendered."
        if has_neighbor_refs else
        "The attached image is the existing rough version of THIS tile."
    )
    return f"""{bible}

You are REFINING one tile of an already-rendered overhead pixel-art map.

{ref_note}

Your job: re-draw THIS tile at high detail while:
  1. Keeping the SAME composition, palette, lighting, and material distribution as the first reference.
  2. Adding the specific features described below.
  3. Keeping every edge MATCHING the existing rough version pixel-by-pixel — the seam to each neighbor must remain continuous.
  4. NO frame, NO border, NO caption, NO vignette. Orthographic top-down. Content runs to all four canvas edges.

TILE ({x},{y}) DESCRIPTION: {tile['description']}
{composition}EDGES (do not contradict; these MUST visually continue from the rough version):
  N={edges.get('N','')}
  E={edges.get('E','')}
  S={edges.get('S','')}
  W={edges.get('W','')}

Output ONE square image only — the refined version of this tile."""


def tile_image_prompt(
    bible: str,
    tile: dict,
    neighbors: dict[str, dict],
) -> str:
    edges = tile.get("edges", {})
    objects = tile.get("objects") or "none"
    composition = "" if objects.lower() == "none" else f"COMPOSITION: {objects}\n"
    adj_lines = []
    for dir_key, label in [("N", "north"), ("E", "east"), ("S", "south"), ("W", "west")]:
        n = neighbors.get(dir_key)
        if n:
            adj_lines.append(f"  {label}-neighbor (type={n['type']}): {n.get('description','')}")
    adj_block = "ADJACENT:\n" + "\n".join(adj_lines) if adj_lines else ""
    fix_block = f"FIX: {tile['fix_reason']}\n" if tile.get("fix_reason") else ""

    return f"""{bible}

CRITICAL RENDERING RULES — read before drawing:
1. This image is ONE SQUARE CROP of a larger continuous overhead map. It is NOT a standalone picture.
2. Orthographic top-down view. No perspective, no vignette, no rounded corners, no drop shadow.
3. NEVER draw a decorative border, frame, outline, matting, or background fill around the image. The content must run FLUSH to all four edges of the canvas and VISUALLY CONTINUE PAST each edge.
4. Obey the EDGES spec literally. For each edge:
     - If the edge says "map boundary" — that side is the natural end of the material (ragged grass/weathered stone end). No wall is drawn unless the CONTENT itself is a wall.
     - If the edge describes a material (cobblestone, dirt, wall, door threshold, plank, etc.) — that material must TOUCH the canvas edge with its full width and be rendered as mid-stroke (not terminated on this tile).
     - If the edge says "floor" / "ground" / "continuous <material>" — draw the ground all the way to the canvas edge. DO NOT add a wall, fence, post, or frame along that edge.
5. If reference images are attached, they are the style+seam authority. Match their palette, lighting direction, and material detail EXACTLY. When a neighbor reference sits on the N/E/S/W side, the shared row/column of pixels must plausibly continue between the two.

CONTENT: {tile['description']}
{composition}EDGES (exactly what occupies each edge — do not contradict):
  N={edges.get('N','')}
  E={edges.get('E','')}
  S={edges.get('S','')}
  W={edges.get('W','')}
{adj_block}
{fix_block}
Render ONLY the map square. Output must be exactly {'' }one square image with no padding, no caption, no legend."""


# ------------------------------------------------------------------ parsing
TILE_LINE_RE = re.compile(r"TILE\s+(\d+)\s*,\s*(\d+)\s*:\s*(.*)", re.IGNORECASE)


def parse_spatial_plan(text: str, size: int) -> dict[tuple[int, int], dict]:
    plan = {}
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line:
            continue
        m = TILE_LINE_RE.match(line)
        if not m:
            continue
        x, y = int(m.group(1)), int(m.group(2))
        if not (0 <= x < size and 0 <= y < size):
            continue
        rest = m.group(3)
        fields = {"description": "", "objects": "none", "edges": {"N": "", "E": "", "S": "", "W": ""}, "interior": ""}
        for segment in rest.split("|"):
            seg = segment.strip()
            if not seg:
                continue
            if "=" not in seg:
                continue
            key, value = seg.split("=", 1)
            key = key.strip().upper()
            value = value.strip()
            if key == "DESC":
                fields["description"] = value
            elif key == "OBJECTS":
                fields["objects"] = value
            elif key in ("N", "E", "S", "W"):
                fields["edges"][key] = value
            elif key == "INTERIOR":
                fields["interior"] = value
        plan[(x, y)] = fields
    return plan


def reconcile_edges(plan: dict, size: int):
    """For each shared seam, pick the more specific phrase and copy it to both sides."""
    for y in range(size):
        for x in range(size):
            if x + 1 < size:
                a = plan[(x, y)]["edges"]["E"]
                b = plan[(x + 1, y)]["edges"]["W"]
                chosen = pick_more_specific(a, b)
                plan[(x, y)]["edges"]["E"] = chosen
                plan[(x + 1, y)]["edges"]["W"] = chosen
            if y + 1 < size:
                a = plan[(x, y)]["edges"]["S"]
                b = plan[(x, y + 1)]["edges"]["N"]
                chosen = pick_more_specific(a, b)
                plan[(x, y)]["edges"]["S"] = chosen
                plan[(x, y + 1)]["edges"]["N"] = chosen
    for x in range(size):
        if not plan[(x, 0)]["edges"]["N"]:
            plan[(x, 0)]["edges"]["N"] = "map boundary"
        if not plan[(x, size - 1)]["edges"]["S"]:
            plan[(x, size - 1)]["edges"]["S"] = "map boundary"
    for y in range(size):
        if not plan[(0, y)]["edges"]["W"]:
            plan[(0, y)]["edges"]["W"] = "map boundary"
        if not plan[(size - 1, y)]["edges"]["E"]:
            plan[(size - 1, y)]["edges"]["E"] = "map boundary"


def pick_more_specific(a: str, b: str) -> str:
    def score(s: str) -> int:
        s = (s or "").strip()
        if not s or s.lower() == "map boundary":
            return -1
        return len(s)
    return a if score(a) >= score(b) else b


# ------------------------------------------------------------------ generation
def generate_tile(
    client: Client,
    bible: str,
    grid: list[list[str]],
    tiles: dict,
    images: dict,
    x: int, y: int,
    anchors: dict[str, Image.Image],
) -> Optional[Image.Image]:
    size = len(grid)
    tile = tiles[(x, y)]
    tile["type"] = grid[y][x]

    neighbors = {}
    for dir_key, dx, dy in [("N", 0, -1), ("E", 1, 0), ("S", 0, 1), ("W", -1, 0)]:
        nx, ny = x + dx, y + dy
        if 0 <= nx < size and 0 <= ny < size:
            neighbors[dir_key] = {
                "type": grid[ny][nx],
                "description": tiles[(nx, ny)].get("description", ""),
            }

    refs = []
    for dir_key in ("N", "E", "S", "W"):
        nx, ny = {"N": (x, y - 1), "E": (x + 1, y), "S": (x, y + 1), "W": (x - 1, y)}[dir_key]
        img = images.get((nx, ny))
        if img is not None:
            refs.append(img)
    anchor_key = "floor" if grid[y][x] == FLOOR else "wall"
    if anchors.get(anchor_key) is not None and anchors[anchor_key] not in refs:
        refs.append(anchors[anchor_key])

    prompt = tile_image_prompt(bible, tile, neighbors)
    return client.image(prompt, references=refs[:4])


def bfs_order(size: int, start: tuple[int, int]) -> list[tuple[int, int]]:
    seen = {start}
    order = [start]
    q = collections.deque([start])
    while q:
        x, y = q.popleft()
        for dx, dy in [(0, -1), (1, 0), (0, 1), (-1, 0)]:
            n = (x + dx, y + dy)
            if 0 <= n[0] < size and 0 <= n[1] < size and n not in seen:
                seen.add(n)
                order.append(n)
                q.append(n)
    return order


def find_first(grid: list[list[str]], target: str) -> Optional[tuple[int, int]]:
    for y, row in enumerate(grid):
        for x, cell in enumerate(row):
            if cell == target:
                return (x, y)
    return None


# ------------------------------------------------------------------ correction
def correction_prompt(bible: str, plan: dict, size: int, max_fix: int) -> str:
    tile_lines = []
    for (x, y), t in sorted(plan.items()):
        e = t["edges"]
        tile_lines.append(
            f"TILE {x},{y}: DESC={t['description']} | OBJECTS={t['objects']} "
            f"| N={e['N']} | E={e['E']} | S={e['S']} | W={e['W']}"
        )
    plan_str = "\n".join(tile_lines)
    return f"""You are reviewing a {size}x{size} rendered pixel-art tile mosaic for coherence.

STYLE BIBLE:
{bible}

INTENDED PLAN:
{plan_str}

The attached image is the mosaic. Identify up to {max_fix} tiles whose RENDERED content
breaks continuity with neighbors (walls that dead-end at seams, paths that misalign,
objects that clip or float, lighting that contradicts the bible).

Return lines in EXACTLY this format, one per tile:
FIX x,y: <one-line reason>

If everything looks coherent, return only the single word: OK"""


FIX_LINE_RE = re.compile(r"FIX\s+(\d+)\s*,\s*(\d+)\s*:\s*(.+)", re.IGNORECASE)


def parse_corrections(text: str) -> list[tuple[int, int, str]]:
    out = []
    for line in text.splitlines():
        m = FIX_LINE_RE.match(line.strip())
        if m:
            out.append((int(m.group(1)), int(m.group(2)), m.group(3).strip()))
    return out


# ------------------------------------------------------------------ big-slice
def big_slice_prompt(theme: str, size: int, bible: str, layout_prose: str) -> str:
    return f"""Render ONE complete overhead pixel-art map as a single square image.

THEME: {theme}

STYLE BIBLE:
{bible}

LAYOUT (described in prose; do NOT draw any letters, numbers, plus signs, hashes, dots, or grid markings):
{layout_prose}

CRITICAL RULES:
- Render as ONE continuous picture with NO internal frames, NO grid lines, NO tile borders, NO captions, NO labels, NO compass roses.
- Orthographic top-down view. No perspective, no vignette, no title card.
- The outer wall is a CONTINUOUS ring; adjacent wall sections join seamlessly with the same material.
- Doorways are simple OPENINGS (gaps) in the wall — the ground passes through. Do not draw any door symbol, plus sign, cross, hinges, or signage in the opening.
- The interior ground is one continuous texture across the whole interior — no internal walls or seams between floor regions.
- Buildings are standalone structures sitting on the floor, drawn proportional to a single map cell.
- The image fills its canvas edge to edge. The outer perimeter of the map meets the canvas edge directly — no matting, no drop shadow, no frame.
- Lighting and color stay uniform across the whole image per the style bible.

Output one square image only — no text or symbols painted onto the image."""


def slice_big_image(big: Image.Image, size: int, tile_px: int) -> dict[tuple[int, int], Image.Image]:
    w, h = big.size
    # use the smaller dimension so we stay square
    side = min(w, h)
    left = (w - side) // 2
    top = (h - side) // 2
    big = big.crop((left, top, left + side, top + side))
    cell = side // size
    out = {}
    for y in range(size):
        for x in range(size):
            box = (x * cell, y * cell, (x + 1) * cell, (y + 1) * cell)
            tile = big.crop(box).resize((tile_px, tile_px), Image.LANCZOS)
            out[(x, y)] = tile
    return out


def run_big_slice(cfg: dict, out_dir: Path):
    load_dotenv(cfg.get("env_file"))
    client = Client.from_env(
        text_model=cfg.get("text_model", "google/gemini-2.5-flash"),
        image_model=cfg.get("image_model", "google/gemini-2.5-flash-image"),
    )
    theme = cfg["theme"]
    size = cfg["size"]
    tile_px = cfg.get("tile_px", 512)
    out_dir.mkdir(parents=True, exist_ok=True)
    log = (out_dir / "run_log.txt").open("w")

    def say(msg):
        print(msg)
        log.write(msg + "\n")
        log.flush()

    say(f"== BIG-SLICE STRATEGY ==  theme={theme!r}  size={size}x{size}")
    say(f"text_model={client.text_model}  image_model={client.image_model}")

    grid = build_layout(size)
    ascii_grid = layout_to_ascii(grid)
    layout_prose = layout_to_prose(grid)
    say("Layout:\n" + ascii_grid)
    say("Layout (prose):\n" + layout_prose)

    say("\n[Phase 1] Style bible...")
    bible = client.chat(style_bible_prompt(theme))
    (out_dir / "style_bible.txt").write_text(bible)
    say(bible)

    say("\n[Phase 2] Rendering whole map as one image...")
    big = client.image(big_slice_prompt(theme, size, bible, layout_prose))
    if big is None:
        say("[ERROR] Big image generation failed. Aborting.")
        log.close()
        return
    (out_dir / "mosaic_raw.png").write_bytes(_png_bytes(big))
    say(f"  big image size = {big.size}")

    say("\n[Phase 3] Slicing into tiles...")
    tiles = slice_big_image(big, size, tile_px)
    for (x, y), t in tiles.items():
        t.save(out_dir / f"tile_{x}_{y}.png")

    rows = [[tiles.get((x, y)) for x in range(size)] for y in range(size)]
    mosaic = stitch(rows, tile_px=tile_px)
    mosaic.save(out_dir / "mosaic.png")
    say(f"\nDone. Output: {out_dir}")
    log.close()


def run_big_slice_refined(cfg: dict, out_dir: Path):
    load_dotenv(cfg.get("env_file"))
    client = Client.from_env(
        text_model=cfg.get("text_model", "google/gemini-2.5-flash"),
        image_model=cfg.get("image_model", "google/gemini-2.5-flash-image"),
    )
    theme = cfg["theme"]
    size = cfg["size"]
    tile_px = cfg.get("tile_px", 512)
    use_neighbors = bool(cfg.get("refine_with_neighbors", True))
    out_dir.mkdir(parents=True, exist_ok=True)
    log = (out_dir / "run_log.txt").open("w")

    def say(msg):
        print(msg)
        log.write(msg + "\n")
        log.flush()

    say(f"== BIG-SLICE-REFINED STRATEGY ==  theme={theme!r}  size={size}x{size}")
    say(f"text_model={client.text_model}  image_model={client.image_model}")
    say(f"refine_with_neighbors={use_neighbors}")

    grid = build_layout(size)
    ascii_grid = layout_to_ascii(grid)
    layout_prose = layout_to_prose(grid)
    say("Layout:\n" + ascii_grid)
    say("Layout (prose):\n" + layout_prose)

    # Phase 1 — style bible
    say("\n[Phase 1] Style bible...")
    bible = client.chat(style_bible_prompt(theme))
    (out_dir / "style_bible.txt").write_text(bible)
    say(bible)

    # Phase 2 — spatial plan + reconcile
    say("\n[Phase 2] Planning tile contents and edges...")
    plan_text = client.chat(spatial_plan_prompt(theme, size, grid, bible))
    (out_dir / "spatial_plan_raw.txt").write_text(plan_text)
    plan = parse_spatial_plan(plan_text, size)
    for y in range(size):
        for x in range(size):
            if (x, y) not in plan:
                plan[(x, y)] = {
                    "description": f"{grid[y][x]} tile at ({x},{y})",
                    "objects": "none",
                    "edges": {"N": "", "E": "", "S": "", "W": ""},
                    "interior": "",
                }
    missing = [(x, y) for y in range(size) for x in range(size) if not plan[(x, y)]["description"]]
    say(f"Parsed tiles: {len(plan)}  missing-desc stubs: {len(missing)}")
    reconcile_edges(plan, size)
    with (out_dir / "spatial_plan.json").open("w") as f:
        json.dump({f"{x},{y}": t for (x, y), t in plan.items()}, f, indent=2)

    # Phase 3 — big-slice render
    say("\n[Phase 3] Rendering whole map as one image...")
    big = client.image(big_slice_prompt(theme, size, bible, layout_prose))
    if big is None:
        say("[ERROR] Big image generation failed. Aborting.")
        log.close()
        return
    (out_dir / "mosaic_raw.png").write_bytes(_png_bytes(big))
    say(f"  big image size = {big.size}")

    # Phase 4 — slice + save coarse + stitch pre-refinement mosaic
    say("\n[Phase 4] Slicing into coarse tiles...")
    coarse = slice_big_image(big, size, tile_px)
    for (x, y), t in coarse.items():
        t.save(out_dir / f"tile_{x}_{y}_coarse.png")
    pre_rows = [[coarse.get((x, y)) for x in range(size)] for y in range(size)]
    pre_mosaic = stitch(pre_rows, tile_px=tile_px)
    pre_mosaic.save(out_dir / "mosaic_pre_refinement.png")

    # Phase 5 — per-tile refinement
    say("\n[Phase 5] Per-tile refinement...")
    refined: dict[tuple[int, int], Image.Image] = {}
    total = size * size
    done = 0
    for y in range(size):
        for x in range(size):
            done += 1
            refs = [coarse[(x, y)]]  # own slice always slot 0
            if use_neighbors:
                for dx, dy in [(0, -1), (1, 0), (0, 1), (-1, 0)]:  # N, E, S, W
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < size and 0 <= ny < size and len(refs) < 4:
                        refs.append(coarse[(nx, ny)])
            say(f"  [{done}/{total}] refine ({x},{y}) type={grid[y][x]} refs={len(refs)}")
            prompt = tile_refinement_prompt(
                bible, plan[(x, y)], x, y,
                has_neighbor_refs=len(refs) > 1,
            )
            img = client.image(prompt, references=refs)
            if img is None:
                say(f"  [WARN] refine ({x},{y}) failed — using coarse slice")
                img = coarse[(x, y)]
            refined[(x, y)] = img
            img.save(out_dir / f"tile_{x}_{y}.png")

    # Phase 6 — stitch refined
    say("\n[Phase 6] Stitching refined mosaic...")
    rows = [[refined.get((x, y)) for x in range(size)] for y in range(size)]
    mosaic = stitch(rows, tile_px=tile_px)
    mosaic.save(out_dir / "mosaic.png")
    say(f"\nDone. Output: {out_dir}")
    log.close()


def _png_bytes(img: Image.Image) -> bytes:
    import io
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


# ------------------------------------------------------------------ driver
def run(cfg: dict, out_dir: Path):
    load_dotenv(cfg.get("env_file"))
    client = Client.from_env(
        text_model=cfg.get("text_model", "google/gemini-2.5-flash"),
        image_model=cfg.get("image_model", "google/gemini-2.5-flash-image"),
    )

    theme = cfg["theme"]
    size = cfg["size"]
    tile_px = cfg.get("tile_px", 512)
    max_corr = cfg.get("max_correction_tiles", 3)
    out_dir.mkdir(parents=True, exist_ok=True)
    log_path = out_dir / "run_log.txt"
    log = log_path.open("w")

    def say(msg):
        print(msg)
        log.write(msg + "\n")
        log.flush()

    say(f"== MAP GEN TESTBED ==  theme={theme!r}  size={size}x{size}")
    say(f"text_model={client.text_model}  image_model={client.image_model}")

    # 1. Layout
    grid = build_layout(size)
    say("Layout:\n" + layout_to_ascii(grid))

    # 2. Style bible
    say("\n[Phase 1] Authoring style bible...")
    bible = client.chat(style_bible_prompt(theme))
    (out_dir / "style_bible.txt").write_text(bible)
    say(bible)

    # 3. Spatial plan
    say("\n[Phase 2] Planning tile contents and edges...")
    plan_text = client.chat(spatial_plan_prompt(theme, size, grid, bible))
    (out_dir / "spatial_plan_raw.txt").write_text(plan_text)
    plan = parse_spatial_plan(plan_text, size)
    # fill missing tiles with stubs so reconcile + BFS can proceed
    for y in range(size):
        for x in range(size):
            if (x, y) not in plan:
                plan[(x, y)] = {
                    "description": f"{grid[y][x]} tile at ({x},{y})",
                    "objects": "none",
                    "edges": {"N": "", "E": "", "S": "", "W": ""},
                    "interior": "",
                }
    missing = [(x, y) for y in range(size) for x in range(size) if not plan[(x, y)]["description"]]
    say(f"Parsed tiles: {len(plan)}  missing-desc stubs: {len(missing)}")

    # 4. Reconcile
    reconcile_edges(plan, size)
    with (out_dir / "spatial_plan.json").open("w") as f:
        json.dump(
            {f"{x},{y}": t for (x, y), t in plan.items()},
            f, indent=2,
        )

    # 5. Anchor pair
    say("\n[Phase 3] Generating anchor tiles...")
    primary = find_first(grid, FLOOR) or (size // 2, size // 2)
    secondary = find_first(grid, WALL) or (0, 0)
    images: dict[tuple[int, int], Image.Image] = {}
    anchors: dict[str, Image.Image] = {"floor": None, "wall": None}

    primary_img = generate_tile(client, bible, grid, plan, images, *primary, anchors=anchors)
    if primary_img is None:
        say("[WARN] Primary anchor failed; using gray placeholder.")
        primary_img = Image.new("RGB", (tile_px, tile_px), (96, 96, 96))
    anchors["floor"] = primary_img
    images[primary] = primary_img
    primary_img.save(out_dir / f"tile_{primary[0]}_{primary[1]}.png")

    secondary_img = generate_tile(client, bible, grid, plan, images, *secondary, anchors=anchors)
    if secondary_img is None:
        say("[WARN] Secondary anchor failed; reusing primary for wall.")
        secondary_img = primary_img
    anchors["wall"] = secondary_img
    images[secondary] = secondary_img
    secondary_img.save(out_dir / f"tile_{secondary[0]}_{secondary[1]}.png")

    # 6. BFS
    say("\n[Phase 4] BFS tile generation...")
    order = bfs_order(size, primary)
    total = size * size
    done = len(images)
    for (x, y) in order:
        if (x, y) in images:
            continue
        say(f"  [{done + 1}/{total}] tile ({x},{y}) type={grid[y][x]}")
        img = generate_tile(client, bible, grid, plan, images, x, y, anchors=anchors)
        if img is None:
            img = Image.new("RGB", (tile_px, tile_px), (160, 40, 40))
        images[(x, y)] = img
        img.save(out_dir / f"tile_{x}_{y}.png")
        done += 1

    # Build grid-order list for stitch: grid[y][x]
    def build_mosaic() -> Image.Image:
        rows = [[images.get((x, y)) for x in range(size)] for y in range(size)]
        return stitch(rows, tile_px=tile_px)

    mosaic = build_mosaic()
    mosaic.save(out_dir / "mosaic_pre_correction.png")

    # 7. Correction
    if max_corr > 0:
        say("\n[Phase 5] Coherence correction pass...")
        review = _multimodal_chat(
            client,
            correction_prompt(bible, plan, size, max_corr),
            [mosaic],
        )
        say(f"Correction review:\n{review}")
        fixes = parse_corrections(review)[:max_corr]
        (out_dir / "correction.txt").write_text(review)
        for (x, y, reason) in fixes:
            if not (0 <= x < size and 0 <= y < size):
                continue
            plan[(x, y)]["fix_reason"] = reason
            say(f"  regenerating ({x},{y}) — {reason}")
            img = generate_tile(client, bible, grid, plan, images, x, y, anchors=anchors)
            if img is not None:
                images[(x, y)] = img
                img.save(out_dir / f"tile_{x}_{y}_fixed.png")
        mosaic = build_mosaic()

    mosaic.save(out_dir / "mosaic.png")
    say(f"\nDone. Output: {out_dir}")
    log.close()


def _multimodal_chat(client: Client, prompt: str, images: list[Image.Image]) -> str:
    """Text-review call that attaches images alongside the text prompt."""
    import base64, io, requests, time
    content = []
    for img in images[:4]:
        buf = io.BytesIO()
        img.convert("RGB").save(buf, format="PNG")
        b64 = base64.b64encode(buf.getvalue()).decode("ascii")
        content.append({"type": "image_url", "image_url": {"url": f"data:image/png;base64,{b64}"}})
    content.append({"type": "text", "text": prompt})
    body = {
        "model": client.text_model,
        "messages": [{"role": "user", "content": content}],
    }
    t0 = time.time()
    resp = requests.post(
        "https://openrouter.ai/api/v1/chat/completions",
        json=body,
        headers={
            "Authorization": f"Bearer {client.api_key}",
            "Content-Type": "application/json",
            "HTTP-Referer": client.referer,
            "X-Title": client.title,
        },
        timeout=client.timeout,
    )
    dt = time.time() - t0
    if resp.status_code != 200:
        return f"(correction skipped: {resp.status_code} {resp.text[:200]})"
    data = resp.json()
    text = data.get("choices", [{}])[0].get("message", {}).get("content", "") or ""
    print(f"  [mmchat {dt:.1f}s]")
    return text


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--config", default=str(Path(__file__).parent / "config.yaml"))
    p.add_argument("--theme", default=None)
    p.add_argument("--size", type=int, default=None)
    p.add_argument("--model", default=None, help="image model override")
    p.add_argument("--text-model", default=None)
    p.add_argument("--out", default=None)
    p.add_argument("--strategy",
                   choices=["tile-bfs", "big-slice", "big-slice-refined"],
                   default="tile-bfs",
                   help="tile-bfs: one image call per tile (~30 calls). "
                        "big-slice: one image call total, sliced into tiles. "
                        "big-slice-refined: big-slice as a coarse base, then per-tile refinement (1+N*N image calls).")
    args = p.parse_args()

    cfg = yaml.safe_load(Path(args.config).read_text())
    if args.theme:
        cfg["theme"] = args.theme
    if args.size:
        cfg["size"] = args.size
    if args.model:
        cfg["image_model"] = args.model
    if args.text_model:
        cfg["text_model"] = args.text_model

    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    tag = {
        "tile-bfs": "tilebfs",
        "big-slice": "bigslice",
        "big-slice-refined": "bigslicerefined",
    }[args.strategy]
    base_out = Path(args.out or cfg.get("output_dir", "out"))
    if not base_out.is_absolute():
        base_out = Path(__file__).parent / base_out
    run_dir = base_out / f"{ts}_{tag}"

    if args.strategy == "big-slice":
        run_big_slice(cfg, run_dir)
    elif args.strategy == "big-slice-refined":
        run_big_slice_refined(cfg, run_dir)
    else:
        run(cfg, run_dir)


if __name__ == "__main__":
    main()
