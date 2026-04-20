# UI Redesign Design Spec

## Context

The current game UI is completely unstyled Unity defaults — white panels, plain text, awkward positioning. The map renders as three narrow horizontal strips because the camera doesn't fit the UI layout. Two competing systems exist (`UIManager` + `DNDLLM.Core.GameManager` vs `ChatUI` + `DnD.Managers.GameManager`), causing confusion. The goal is a cohesive, atmospheric D&D UI that looks intentional and makes the game pleasant to play.

---

## Layout

**Side-by-side split** (60/40) within a single `Canvas` (Screen Space Overlay, 1920×1080 reference resolution):

```
Canvas
├── HorizontalLayoutGroup (fills screen, no padding)
│   ├── MapPanel (flex 60%)
│   │   ├── PanelBackground   — dark brown bg, 1px gold border
│   │   ├── PanelHeader       — "✦ THE MAP ✦" gold TMP label
│   │   └── RawImage          — displays MapCamera RenderTexture
│   └── ChatPanel (flex 40%)
│       ├── PanelBackground   — near-black brown bg, 1px gold border
│       ├── PanelHeader       — "✦ THE DUNGEON MASTER ✦" gold TMP label
│       ├── ScrollView        — message history, scrollbars hidden
│       │   └── Content       — VerticalLayoutGroup, ContentSizeFitter
│       └── InputRow
│           ├── TMP_InputField — dark bg, amber text, gold border on focus
│           └── SendButton     — gold bg, dark text "▶"
```

**Map display fix:** A dedicated `MapCamera` (orthographic, top-down) renders to a `RenderTexture` asset (1024×1024). A `RawImage` component in `MapPanel` displays it, filling the panel exactly. This replaces the current broken layout where the main camera tries to show tiles inside the game view directly.

---

## Color Palette

| Role | Hex | Usage |
|------|-----|-------|
| Background deep | `#1A1005` | Chat panel background |
| Background mid | `#1E1508` | Map panel background |
| Background message DM | `#2A1F0E` | DM message bubble bg |
| Background message player | `#1A0F05` | Player message bubble bg |
| Gold accent | `#C8A050` | Headers, borders, send button bg |
| Gold text | `#D4B87A` | DM message text |
| Player text | `#F0D090` | Player message text |
| System text | `#A08060` | System/status messages |
| Input text | `#E8D0A0` | Input field text |
| Placeholder text | `#6B5030` | Input placeholder |

---

## Typography

All text uses **TextMeshPro**.

| Element | Size | Style | Color |
|---------|------|-------|-------|
| Panel headers | 14pt | Uppercase, letter-spacing 2 | `#C8A050` |
| DM messages | 16pt | Italic | `#D4B87A` |
| Player messages | 16pt | Normal, right-aligned | `#F0D090` |
| System messages | 13pt | Italic, centered | `#A08060` |
| Input field | 15pt | Normal | `#E8D0A0` |
| Input placeholder | 15pt | Italic | `#6B5030` |

---

## Message Styling

`ChatUI.cs` already supports three message types via separate prefabs. Each prefab gets restyled:

- **DM message**: Dark brown bubble (`#2A1F0E`), left-aligned, italic gold text, uses existing typewriter effect (0.03s/char)
- **Player message**: Darker bubble (`#1A0F05`), right-aligned, lighter gold text
- **System message**: No bubble background, centered, small italic amber text, used for state transitions ("⚙ Generating map...", "✦ Adventure begins...")

---

## Loading States

`MapGenerator.GenerateMap()` fires `ChatUI.Instance.AddSystemMessage("⚙ Generating map...")` at the start of generation. When generation completes it fires `ChatUI.Instance.AddSystemMessage("✦ Map ready.")` before advancing the game state.

---

## Code Changes

| File | Action |
|------|--------|
| `Assets/Scripts/UI/ChatUI.cs` | Apply theme colors to message prefabs; update input/button styling; add `ShowLoadingMessage` / `ClearLoadingMessage` helpers |
| `Assets/Scripts/UI/UIManager.cs` | **Delete** |
| `Assets/Scripts/Core/GameManager.cs` | **Delete** (legacy — `DnD.Managers.GameManager` takes over) |
| `Assets/Scripts/Map/MapGenerator.cs` | Add `MapCamera` RenderTexture setup; fire loading system messages |
| `masterscene.unity` | Rebuild Canvas hierarchy; add MapCamera; wire RenderTexture to RawImage |

`DnD.Managers.GameManager` and `ChatUI` remain as-is structurally — only visual properties change.

---

## Verification

1. Enter Play mode — game view shows side-by-side layout (no white boxes)
2. Map panel displays the 10×10 grid correctly via RenderTexture (not strips)
3. "⚙ Generating map..." appears as a system message during generation
4. DM messages appear with typewriter effect, styled in italic gold
5. Player messages appear right-aligned in lighter gold
6. Input field and send button match the parchment theme
7. No legacy `UIManager` or `DNDLLM.Core.GameManager` errors in Console
