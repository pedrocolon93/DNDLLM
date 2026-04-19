# Character Creation Popup, Save/Load System & Title Screen — Design Spec

## Goal

Replace the text-based character creation flow with a step-by-step popup wizard that generates a character portrait. Add a persistent save system (3 named slots) that stores character data, conversation history, and the game seed. Add a full-screen title screen on launch for New Game / Continue.

## Architecture

Four new units, two modified files:

| Unit | File | Responsibility |
|------|------|----------------|
| `SaveData` | `Assets/Scripts/Data/SaveData.cs` | Serializable data model for one save slot |
| `SaveSystem` | `Assets/Scripts/Services/SaveSystem.cs` | Read/write JSON + portrait PNG to disk |
| `CharacterCreationPopup` | `Assets/Scripts/UI/CharacterCreationPopup.cs` | Step-by-step wizard MonoBehaviour |
| `TitleScreen` | `Assets/Scripts/UI/TitleScreen.cs` | Launch screen + slot list MonoBehaviour |
| `GameManager` | `Assets/Scripts/Managers/GameManager.cs` | Integrate popup, title screen, save/load |
| `UISceneBuilder` | `Assets/Editor/UISceneBuilder.cs` | Build title screen + popup canvas hierarchy |

---

## 1. Data Model — `SaveData`

Plain C# class (no MonoBehaviour), `[System.Serializable]`, serialised via `JsonUtility`.

```csharp
[System.Serializable]
public class SaveData
{
    // Slot metadata
    public int    slotIndex;       // 0, 1, or 2
    public string slotLabel;       // e.g. "Aric · Fighter · Lv3"
    public string lastPlayed;      // ISO-8601 UTC string

    // Campaign
    public string campaignSeed;     // player's initial campaign prompt
    public string campaignTimeline; // DM's generated campaign intro text

    // Character identity
    public string characterName;
    public string raceName;         // enum .ToString()
    public string className;        // enum .ToString()
    public string appearanceDescription;
    public string backstory;

    // Character stats (snapshot)
    public int level;
    public int maxHP;
    public int currentHP;
    public int armorClass;
    public int str, dex, con, intel, wis, cha;

    // Game state
    public string gameState;        // GameState enum .ToString()

    // Conversation history
    public List<ChatMessageData> messages;
}

[System.Serializable]
public class ChatMessageData
{
    public string type;  // "Player", "DM", or "System"
    public string text;
}
```

No portrait bytes in the JSON — the portrait is stored as a separate `slot_N_portrait.png` alongside the JSON file.

---

## 2. Save System — `SaveSystem`

Static class. Storage root: `Application.persistentDataPath/Saves/`.

**Public API:**

```csharp
// Returns SaveData or null for each of the 3 slots (index 0-2)
public static SaveData[]  LoadAllSlots();

// Saves data + optional portrait for slot index
public static void        Save(int slotIndex, SaveData data, Texture2D portrait);

// Loads a single slot's data + portrait (portrait may be null)
public static (SaveData data, Texture2D portrait) Load(int slotIndex);

// Erases slot files
public static void        Delete(int slotIndex);
```

**File layout:**
```
persistentDataPath/Saves/slot_0.json
persistentDataPath/Saves/slot_0_portrait.png
persistentDataPath/Saves/slot_1.json
persistentDataPath/Saves/slot_1_portrait.png
persistentDataPath/Saves/slot_2.json
persistentDataPath/Saves/slot_2_portrait.png
```

---

## 3. Character Creation Wizard — `CharacterCreationPopup`

MonoBehaviour attached to a full-screen Canvas that sits above the main game canvas (`sortingOrder = 10`).

**Steps (6 total, driven by an `int _step` counter):**

| Step | Content |
|------|---------|
| 1 | Name — TMP_InputField, free text |
| 2 | Race — button grid (Human, Elf, Dwarf, Halfling, Dragonborn, Gnome, Half-Elf, Half-Orc, Tiefling) |
| 3 | Class — button grid (Fighter, Wizard, Rogue, Cleric, Barbarian, Ranger, Paladin, Monk, Bard, Druid, Warlock, Sorcerer) |
| 4 | Appearance — TMP_InputField multiline textarea; placeholder: "Describe your hero's appearance…" |
| 5 | Backstory — TMP_InputField multiline textarea; placeholder: "What brought your hero to this adventure?" |
| 6 | Confirm — portrait RawImage (shows spinner while generating), stat summary, "Begin Adventure" button (disabled until portrait returns or times out after 30 s) |

**Step indicator:** 5 horizontal bar segments at the top, one per content step (1–5). Step 6 (Confirm) shows all 5 bars filled — it is the completed state, not a new segment.

**Events:**
```csharp
public System.Action<CharacterCreationData> OnComplete;
public System.Action                        OnCancelled;
```

`CharacterCreationData` is a plain struct carrying all wizard field values + the generated portrait:

```csharp
public struct CharacterCreationData
{
    public string    characterName;
    public Race      race;
    public CharacterClassName characterClass;
    public string    appearanceDescription;
    public string    backstory;
    public Texture2D portrait;   // null if generation timed out
}
```

**Portrait generation:** On entering step 6, call `LLMService.Instance.GenerateImage(portraitPrompt)` asynchronously. Portrait prompt is built from appearance + class + race: `"Fantasy RPG character portrait, {race} {class}, {appearanceDescription}, painterly art style, face and shoulders, plain dark background."` While generating, the RawImage shows a placeholder with pulsing alpha. On completion, display the portrait and enable "Begin Adventure".

**Back/Next:** Each step validates before advancing (name must be non-empty; race/class must be selected). Back is always available from steps 2–5.

**Cancel:** An "×" button on steps 1–5. Step 6 has no cancel (portrait is generating; user must wait or the 30 s timeout completes with a fallback grey texture).

**Slot selection:** The popup receives `int targetSlotIndex` from `TitleScreen` so `GameManager` knows which slot to write on completion.

---

## 4. Title Screen — `TitleScreen`

MonoBehaviour on a full-screen Canvas (`sortingOrder = 20`, renders above everything).

**Layout:**
- Centered logo: "D&D LLM" + subtitle "AN AI ADVENTURE"
- "+ NEW GAME" button (gold, full width) → picks the first empty slot; if all full, asks which to overwrite (simple three-button modal)
- 3 slot rows; each row shows:
  - 36×36 portrait thumbnail (or grey placeholder if no portrait)
  - Character name, class, level, race
  - Campaign name (first 30 chars of `campaignSeed`)
  - Last played date (formatted as "N days ago" / "Today")
  - "›" chevron — clicking loads slot and hides title screen
- Empty slots show greyed "Empty slot" text and are not clickable

**Visibility:**
- Shown on `GameState.MainMenu`
- Hidden when a slot is loaded or New Game begins
- Re-shown when the in-game "Menu" button is pressed (saves current game first, then shows slots)

**In-game Menu button:** Small button anchored top-right of the main Canvas. Label: "MENU". Clicking it: (1) saves current slot to disk, (2) shows TitleScreen.

---

## 5. GameManager Integration

`GameManager.ChangeState(GameState.MainMenu)` now:
1. Shows `TitleScreen` instead of posting chat messages.

`TitleScreen` "New Game" path:
1. Player types campaign prompt in the existing chat (unchanged)
2. `StartCampaignAsync` runs, DM responds with campaign intro
3. `GameManager.ChangeState(GameState.CharacterCreation)` now shows `CharacterCreationPopup` instead of chat prompts
4. On `OnComplete`: apply character data to `playerCharacter`, auto-roll ability scores, call `SaveSystem.Save(slotIndex, data, portrait)`, transition to `GameState.Exploration`

`TitleScreen` "Load Slot" path:
1. `SaveSystem.Load(slotIndex)` → populate `playerCharacter` + `ChatUI` messages
2. `ChatUI.AddSystemMessage("--- Adventure resumed ---")`
3. `ChangeState(savedData.gameState parsed back to enum)`

---

## 6. UISceneBuilder Changes

Two new `[MenuItem]` methods:

- **"DnD/Build Title Screen"** — creates a second Canvas (`sortingOrder = 20`) with the TitleScreen layout wired up
- **"DnD/Build Character Popup"** — creates a third Canvas (`sortingOrder = 10`) with the wizard layout (step panels hidden by default, only step 1 visible)

Existing **"DnD/Rebuild UI Canvas"** gains a small "MENU" button (top-right, anchored).

---

## File Summary

**Create:**
- `Assets/Scripts/Data/SaveData.cs`
- `Assets/Scripts/Services/SaveSystem.cs`
- `Assets/Scripts/UI/CharacterCreationPopup.cs`
- `Assets/Scripts/UI/TitleScreen.cs`

**Modify:**
- `Assets/Scripts/Managers/GameManager.cs` — integrate popup + title screen + save/load on state transitions
- `Assets/Editor/UISceneBuilder.cs` — add two new menu items + MENU button to existing canvas

---

## Out of Scope

- Inventory, quest log, or combat state persistence (saved `gameState` string lets future work resume combat, but combat manager state is not serialised in this spec)
- Cloud sync or multiple profiles
- Slot renaming after creation
