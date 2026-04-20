# Setup Guide

How to get the project running from a fresh clone. The scene is built by an editor script — you do not need to drag components by hand.

---

## 1. Requirements

- Unity **2022.3 LTS** or newer (2D template or any template works).
- An OpenRouter API key (for chat + image generation), **or** a running local Ollama instance for chat-only play. Image generation always requires OpenRouter.

---

## 2. First-time scene setup

1. Open the project in Unity. Open any scene in `Assets/` (e.g. `masterscene.unity`) or create an empty one.
2. Menu bar → **DnD → Rebuild UI Canvas**.

   This runs `Assets/Editor/UISceneBuilder.cs`, which:
   - deletes any existing Canvas,
   - rebuilds the horizontal split (map on the left, chat on the right),
   - wires up `ChatUI`, `TitleScreen`, `CharacterCreationPopup`, `InGameMenuPanel`, `EditMapPanel`, and `CharacterScreenPanel`,
   - creates/updates the `GameManager`, `LLMService`, and `MapGenerator` GameObjects and cross-references them.

3. Save the scene.

If you add new UI panels or rewire references, re-run **DnD → Rebuild UI Canvas**; it is idempotent.

---

## 3. Configure the LLM backend

Select the `LLMService` GameObject in the hierarchy. Important inspector fields:

| Field | Meaning | Typical value |
|------|---------|---------------|
| `Provider` | `OpenRouter` or `Ollama` (text only) | `OpenRouter` |
| `Use Mock` | If checked, `SendPrompt` returns `[MOCK] <input>` and `GenerateImage` returns a random-colour square. Good for UI work without API cost. | off |
| `Api Key` | OpenRouter bearer token (`sk-or-v1-…`) | your key |
| `Model` | Chat model for OpenRouter | `openai/gpt-4o-mini` |
| `Image Model` | Used by `GenerateImage` / `GenerateStyledTile`. Gemini models enable the multimodal style-anchor path. | `google/gemini-2.5-flash-image-preview` |
| `Ollama Base Url` | Only used when `Provider = Ollama` | `http://localhost:11434` |
| `Ollama Model` | Local model tag | `llama3.2` |
| `Use Cache` | Persist generated images under `Application.persistentDataPath/ImageCache/` keyed by prompt | on |

See [`LLM_INTEGRATION_GUIDE.md`](LLM_INTEGRATION_GUIDE.md) for provider trade-offs and the reasoning behind the Gemini-specific path.

---

## 4. (Optional) Configure TTS

Select the `GameSystem` GameObject and find `TTSService`:

| Field | Meaning |
|------|---------|
| `Enabled` | Master off switch. Play buttons disappear from DM bubbles when off. |
| `Model` | OpenRouter slug. Default `openai/gpt-audio`. |
| `Voice` | One of `alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer`. |
| `Format` | `wav` (only supported format today). |
| `Auto Play` | If on, every new DM message auto-speaks. Mirrored to the in-game menu and persisted per slot. |
| `Volume` | `AudioSource.volume` applied to playback. |
| `Use Cache` | Persist generated WAVs under `Application.persistentDataPath/AudioCache/` keyed by SHA256 of text. |

TTS reuses `LLMService.ApiKey` — no second token required.

---

## 5. Configure the GameManager (optional)

Select the `GameManager` GameObject. The `Use Mock LLM` toggle controls which `ILLMProvider` implementation is handed to `DungeonMaster` / `CommandParser` for campaign-timeline and command-parsing calls. Today only `MockLLMProvider` is wired in — the live path goes through `LLMService` regardless — so leave this checked.

UI references (`TitleScreen`, `CharacterCreationPopup`, `InGameMenuPanel`, `EditMapPanel`, `CharacterScreenPanel`, buttons) are populated automatically by **DnD → Rebuild UI Canvas**.

---

## 6. Play loop

1. Press **Play**. The title screen shows three slots. Empty slots show **+ New**; full slots show the character label and portrait.
2. **New Game** → type a campaign prompt in the chat box (e.g. *"a haunted lighthouse on a storm-wracked cliff"*). `GameManager.StartCampaignAsync` asks `DungeonMaster` for a timeline, then opens `CharacterCreationPopup`.
3. Fill in the popup (name, race, class, ability scores, appearance, backstory). A portrait is generated in the background via `LLMService.GenerateImage`.
4. On completion the game transitions to `Exploration`:
   - `MapGenerator` draws a 7×7 grid and generates tile art in style.
   - The DM narrates the opening scene, including a `[GM_ACTIONS]` block if needed (see LLM guide).
   - NPC / enemy tokens are spawned in parallel via `GameManager.SpawnMapEntitiesAsync`.
   - An autosave fires.
5. Type natural-language actions. Direction words (`north`, `go west`, `ne`) also move the character token before the DM call runs. Stepping onto a `Door` or `Exit` tile short-circuits straight into sub-map traversal; every other input also triggers DM narration via `LLMService.SendPrompt`.
6. Stepping onto a `Door` tile, or a DM `ENTER_SUBREGION` command, pushes a new map onto `MapGraph`. Stepping onto an `Exit` tile pops back to the parent map at the position you left.
7. **Menu** button (top-right) opens `InGameMenuPanel` with **Save**, **Load**, **Regenerate Tile** at the current position. **Character** button opens the character sheet. **Edit Map** button opens the tile painter.

---

## 7. Where things live

| Path | What's in it |
|------|--------------|
| `Application.persistentDataPath/Saves/slot_{0,1,2}.json` | Character sheet, campaign seed, DM timeline text, chat history, per-tile type + description overrides |
| `Application.persistentDataPath/Saves/slot_{0,1,2}_portrait.png` | Character portrait or generated map token |
| `Application.persistentDataPath/ImageCache/` | Prompt-keyed PNG cache of generated images |

macOS path: `~/Library/Application Support/<company>/<product>/`.

---

## 8. Troubleshooting

- **Title screen and slots not appearing.** Re-run **DnD → Rebuild UI Canvas** and save the scene. Check the top-left debug label — if it says `ChatUI MISSING`, the canvas build failed.
- **Tile generation looks generic / breaks style.** The style anchor is the first Floor tile. If `Image Model` does not start with `google/`, `GenerateStyledTile` falls back to `GenerateImage` (prompt-only, no image reference) and the art style drifts. Use a Gemini image model.
- **Blank / error responses during exploration.** Check the Unity console for `[LLMService] Chat error` — the error body is logged verbatim. Usually the API key, model slug, or rate limit.
- **Ollama returns errors.** Ollama's OpenAI-compatible endpoint requires `/v1/chat/completions`. `LLMService` builds that URL automatically from `Ollama Base Url`; confirm your model is pulled (`ollama pull llama3.2`).
- **Stale layout after editing UI scripts.** Always re-run **DnD → Rebuild UI Canvas** after changing `UISceneBuilder.cs` — the scene captures the built output, not the script.

---

## 9. Rebuilding or wiping saves

Delete individual slots from the title screen (each slot row has a delete button). To wipe everything, remove the `Saves/` folder at `Application.persistentDataPath`.
