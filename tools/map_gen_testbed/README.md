# Map Generation Testbed

Iterate the 5-phase map generation pipeline (style bible → spatial plan → anchor pair → BFS → correction) outside Unity. Output is a stitched mosaic PNG per run, so you can eyeball edge continuity without a play-mode cycle.

## Setup

```bash
cd tools/map_gen_testbed
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
```

The script reads `OPENROUTER_API_KEY` from env. Point `env_file` in `config.yaml` (already defaults to `/Users/pedro/PycharmProjects/fintech2/.env`) or export manually:

```bash
export OPENROUTER_API_KEY=sk-or-v1-...
```

## Strategies

Three pipelines are available via `--strategy`:

- `tile-bfs` — one image call per tile (~N² calls). Each tile sees its neighbors as references. Tends to produce framed mini-scenes with broken seams.
- `big-slice` — one image call total. The whole map is rendered as one image then sliced into tiles. Coherent by construction at 5×5; loses per-tile detail at larger N. Currently the Unity port.
- `big-slice-refined` — big-slice as a coarse base + a per-tile refinement pass that injects features from the spatial plan. 1 + N² image calls. Each refinement call attaches its own coarse slice as the composition anchor (slot 0) and up to 3 neighbor coarse slices for seam continuity. Aimed at scaling past 5×5.

## Run

```bash
python generate.py                                  # uses config.yaml defaults (tile-bfs)
python generate.py --strategy big-slice                                       # one-shot mosaic
python generate.py --strategy big-slice-refined --theme "cursed crypt" --size 7
python generate.py --model google/gemini-2.5-flash-image                      # override image model
python generate.py --model black-forest-labs/flux.2-pro                        # A/B with FLUX (default in config.yaml)
```

The default image model in `config.yaml` is `black-forest-labs/flux.2-pro`. The OpenRouter client auto-sets `modalities=["image"]` for FLUX (image-only output) and keeps `["image","text"]` for Gemini's image model.

Output lives in `out/<YYYYMMDD_HHMMSS>_<tag>/` where tag is `tilebfs`, `bigslice`, or `bigslicerefined`.

Common artifacts:

- `style_bible.txt` — LLM-authored palette/materials/lighting/motifs.
- `spatial_plan.json` — parsed per-tile DESC/OBJECTS/EDGES/INTERIOR (tile-bfs and big-slice-refined).
- `spatial_plan_raw.txt` — raw LLM spatial-plan output (for debugging parse issues).
- `run_log.txt` — chronological log of every phase + latency.
- `mosaic.png` — final stitched grid.

Strategy-specific:

- `tile-bfs`: `tile_<x>_<y>.png`, `tile_<x>_<y>_fixed.png`, `mosaic_pre_correction.png`, `correction.txt`.
- `big-slice`: `mosaic_raw.png` (uncropped LLM output), `tile_<x>_<y>.png` (slices).
- `big-slice-refined`: `mosaic_raw.png`, `tile_<x>_<y>_coarse.png` (slices), `mosaic_pre_refinement.png` (stitched coarse, A/B reference), `tile_<x>_<y>.png` (refined).

## What to look at

Edge continuity in `mosaic.png`:
- Walls exit one tile and continue into the neighbor at the same thickness/material.
- Cobblestone / dirt paths cross seams without cutoff.
- Lighting direction is consistent across all 25 tiles.

If a theme produces a bad mosaic, tune:
- The style-bible prompt (`style_bible_prompt()` in `generate.py`).
- The spatial-plan prompt — especially the EDGE matching rules.
- The per-tile image prompt's `PERSPECTIVE_LOCK` + `EDGES:` block (`tile_image_prompt`).
- The refinement prompt (`tile_refinement_prompt`) — emphasis on "match the first ref pixel-by-pixel at the seams".
- For `big-slice-refined`: toggle `refine_with_neighbors` in `config.yaml` to swap between own-only refs and own + 3 neighbors.

Once a run passes eyeball inspection on 2-3 themes, port the prompt changes into `Assets/Scripts/Map/MapGenerator.cs` and re-verify in Unity.

## Files

- `generate.py` — driver and pipeline.
- `openrouter_client.py` — OpenRouter `/chat/completions` wrapper (text + multimodal image).
- `stitch.py` — PIL grid composer.
- `config.yaml` — defaults.
