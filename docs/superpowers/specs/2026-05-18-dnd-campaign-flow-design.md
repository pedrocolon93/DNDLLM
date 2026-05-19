# D&D Campaign Flow — Design

**Date:** 2026-05-18
**Goal:** End-to-end flow from user seed → structured campaign plan → sized map → starting positions → turn-based interaction loop, using local LLM at `localhost:1337` and procedural debug sprites instead of generated images.

## What already exists (informs scope)

The codebase has substantial scaffolding in place. This design layers onto it rather than replacing it.

- `GameManager` — state machine (MainMenu → CharacterCreation → Exploration → Combat → Dialogue), party + TurnQueue, 3-slot save system
- `LLMService` — OpenRouter and Ollama paths; tool-aware chat completions
- `DungeonMaster` — campaign generation, action narration, tool-call loop
- `MapGenerator` — Strategy D (LLM logical grid + holistic painted map) and per-tile fallback
- `MapCharacterController`, `MapEntityController`, `MapGraph` — tokens + sub-region traversal
- `GMToolExecutor` — 12 GM tools (MOVE/DAMAGE/HEAL/SPAWN_ENEMY/…)
- UI: `TitleScreen`, `AdventurePromptPopup`, `CharacterCreationPopup` (multi-step + QuickCreate), `InGameMenuPanel`, `EditMapPanel`, `CharacterScreenPanel`, `ChatUI`
- `CombatManager` — D&D 5e initiative + attack

## Gaps

1. `LLMService` cannot talk to `localhost:1337` (needs Bearer-auth OpenAI-compatible path)
2. Every image (tiles, characters, entities, map background, portraits) currently round-trips a remote image API — no debug-sprite fallback
3. `StoryTimeline` is free-text only; map generation receives the seed but no structured features list
4. No campaign-size selector; map is fixed at 7×7

## Decisions (locked in via clarifying questions)

- Debug sprites = default ON, single `LLMService.useDebugSprites` toggle
- Single-player only (party scaffolding stays untouched)
- Default model: `qwen3.6-35b-a3b-mxfp4` on `localhost:1337`

## Architecture

### Component map

```
AdventurePromptPopup       UISceneBuilder
   │   (seed, size)            (builds size buttons)
   ▼
GameManager.StartCampaignAsync(seed, size)
   │
   ▼
DungeonMaster.GenerateCampaignAsync(seed, size, level) → CampaignPlan
   │       (JSON via LLMService.Instance.SendPrompt → Local provider)
   ▼
CharacterCreationPopup (unchanged)
   │
   ▼
GameManager.StartExploration
   │  (sets MapGenerator size + plan.keyLocations)
   ▼
MapGenerator.GenerateMap(seed)
   │  (Strategy D path → LogicalGrid generation gets keyLocations)
   │  (Holistic paint phase replaced by DebugSpriteFactory when useDebugSprites)
   ▼
OnMapReady → opening narration + SpawnCharacter + SpawnEntities
   │  (entity tokens via DebugSpriteFactory when useDebugSprites)
   ▼
Interaction loop: ChatUI input → DungeonMaster.RunPlayerTurnAsync(tool loop)
   │  (LLMService.Instance.ChatWithToolsAsync → Local provider)
   ▼
TurnQueue.AdvanceTurn (single-player rotates trivially)
```

### New units

**`DebugSpriteFactory`** (`Assets/Scripts/Utils/DebugSpriteFactory.cs`)
Pure-procedural Texture2D generator. One public method per artifact:
- `MakeTile(string terrainType, string feature)` → 64×64 colored square + optional shape badge
- `MakeMap(LogicalGrid grid, int pixelsPerCell)` → composite NxN grid as a single texture
- `MakeCharacterToken(Color tint)` → 64×64 transparent square with a filled circle
- `MakeEntityToken(bool isEnemy)` → triangle (enemy red) or diamond (NPC green)
- `MakePortrait(string label)` → 256×256 placeholder with a class glyph

Terrain → color palette is keyword-keyed (grass green, stone grey, water blue, wall dark grey, sand tan, wood brown, cobble pale grey).
Feature → shape and color (chest gold diamond, tavern warm orange square, monster red triangle, exit purple cross, fountain blue circle, etc.).

**`CampaignSize`** enum + **`CampaignPlan`** class (`Assets/Scripts/AI/CampaignPlan.cs`)
```csharp
public enum CampaignSize { Small, Medium, Large }
[Serializable] public class CampaignPlan {
    public string         seed;
    public CampaignSize   size;
    public string         hook;
    public List<string>   beats;
    public string         climax;
    public string         resolution;
    public string         startingArea;
    public List<string>   keyLocations;
    public List<string>   keyNPCs;
    public string         timelineText;   // human-readable, displayed in chat
}
```
Size → `(mapWidth, mapHeight, beatCount)`: Small `(5,5,3)`, Medium `(7,7,5)`, Large `(9,9,7)`.

**`LLMProvider.Local`** branch in `LLMService`
- New fields: `localBaseUrl` (default `http://127.0.0.1:1337`), `localApiKey` (Bearer), `localModel` (default `qwen3.6-35b-a3b-mxfp4`)
- Routes through existing `SendChatCompletionAsync` and `ChatWithToolsAsync` helpers (URL + auth selection only)
- Default `provider = LLMProvider.Local`

### Modified units

**`LLMService`**
- Add `useDebugSprites` field, default `true`
- `GenerateImage`, `GenerateStyledTile`, `GenerateHolisticMapAsync`, `EvaluateMapImageAsync`, `RefineMapImageAsync` short-circuit to `DebugSpriteFactory` when flag is ON. Eval returns `"PERFECT"` (skips refinement loop).

**`DungeonMaster`**
- New `GenerateCampaignAsync(string seed, CampaignSize size, int level) → CampaignPlan`
- Asks LLM for JSON matching the `CampaignPlan` schema, strips ```` ``` ```` fences, parses via `JsonUtility`, gracefully falls back to a minimal `CampaignPlan` (seed + size + a one-line `timelineText`) on parse failure
- Old `GenerateCampaignAsync(string, int) → StoryTimeline` overload stays for any existing call sites
- Routes through `LLMService.Instance.SendPrompt` directly (no `ILLMProvider` indirection) so the configured provider drives campaign gen

**`AdventurePromptPopup`**
- Three buttons: `smallButton`, `mediumButton`, `largeButton` (Medium selected by default)
- `OnSubmit` signature becomes `Action<string, CampaignSize>`
- Selected size highlighted with `BtnSelected` color

**`UISceneBuilder`**
- Constructs the three size buttons inside `AdventurePromptPopup`, wires the references

**`GameManager`**
- Stores `_campaignSize` and `_campaignPlan`
- `OnAdventurePromptSubmitted(string, CampaignSize)` updates signature
- `StartCampaignAsync(string, CampaignSize)` → calls `DungeonMaster.GenerateCampaignAsync(seed, size, level)`
- `StartExploration` sets `MapGenerator.width/height` from `_campaignPlan.size` and `MapGenerator.KeyLocations = plan.keyLocations` before calling `GenerateMap(seed)`
- Save data gains `campaignSize` and `campaignPlanJson` fields (additive; old saves still load)

**`MapGenerator`**
- New `KeyLocations` field, plumbed into `LLMService.GenerateLogicalGridAsync` (which is also extended to take a `requiredFeatures` list)
- When `LLMService.useDebugSprites` is on, `Strategy D` skips holistic paint + evaluate/refine entirely and renders the logical grid via `DebugSpriteFactory.MakeMap`

**`SaveData`**
- Add `campaignSize` (string) and `campaignPlanJson` (string)
- Backward compatible: missing fields default cleanly

## Error handling

- `localhost:1337` unreachable → `LLMService` logs error and returns `"Error calling LLM API."`; GameManager treats this as a generation failure and creates a minimal `CampaignPlan` with `timelineText = "The Dungeon Master gathers thoughts..."`
- Malformed JSON from the LLM → fallback to a minimal `CampaignPlan` (seed-derived)
- `DebugSpriteFactory` never throws — uses `Texture2D.whiteTexture` as last resort

## Out of scope

- Multiplayer / hot-seat party UI (single-player only)
- Image-gen pipeline replacement (still works when `useDebugSprites = false`)
- Combat or save-file rewrite
- Any LLMUnity / OpenAI Provider work
