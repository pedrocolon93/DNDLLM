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

## Run

```bash
python generate.py                                  # uses config.yaml defaults
python generate.py --theme "cursed crypt" --size 5
python generate.py --model google/gemini-2.5-flash-image  # override image model
python generate.py --model google/gemini-3.0-pro-image    # A/B with 3.x
```

Output lives in `out/<YYYYMMDD_HHMMSS>/`:

- `tile_<x>_<y>.png` — each individual tile.
- `tile_<x>_<y>_fixed.png` — tiles the correction pass regenerated.
- `mosaic.png` — stitched grid (final, post-correction).
- `mosaic_pre_correction.png` — stitched grid before correction (A/B the fix).
- `style_bible.txt` — LLM-authored palette/materials/lighting/motifs.
- `spatial_plan.json` — parsed per-tile DESC/OBJECTS/EDGES/INTERIOR.
- `spatial_plan_raw.txt` — raw LLM spatial-plan output (for debugging parse issues).
- `correction.txt` — correction-pass LLM output and reasons.
- `run_log.txt` — chronological log of every phase + latency.

## What to look at

Edge continuity in `mosaic.png`:
- Walls exit one tile and continue into the neighbor at the same thickness/material.
- Cobblestone / dirt paths cross seams without cutoff.
- Lighting direction is consistent across all 25 tiles.

If a theme produces a bad mosaic, tune:
- The style-bible prompt (`style_bible_prompt()` in `generate.py`).
- The spatial-plan prompt — especially the EDGE matching rules.
- The per-tile image prompt's `PERSPECTIVE_LOCK` + `EDGES:` block.

Once a run passes eyeball inspection on 2-3 themes, port the prompt changes into `Assets/Scripts/Map/MapGenerator.cs` and re-verify in Unity.

## Files

- `generate.py` — driver and pipeline.
- `openrouter_client.py` — OpenRouter `/chat/completions` wrapper (text + multimodal image).
- `stitch.py` — PIL grid composer.
- `config.yaml` — defaults.
