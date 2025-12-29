# Unity Scene Setup Guide

This guide walks you through setting up a complete D&D game scene from scratch.

## Quick Start Scene Setup

### 1. Create Main Scene

1. `File > New Scene > 2D` or use existing scene
2. Save as `MainGame.unity` in `Assets/Scenes/`

### 2. Create Game Systems GameObject

Create the core game management systems:

```
Hierarchy:
└── GameSystems (Empty GameObject)
    ├── GameManager (Add Component)
    ├── DungeonMaster (Add Component)
    ├── CombatManager (Add Component)
    └── CommandParser (Add Component)
```

**Steps:**
1. Right-click Hierarchy > Create Empty
2. Rename to "GameSystems"
3. Add Component > Search "GameManager"
4. Add Component > Search "DungeonMaster"
5. Add Component > Search "CombatManager"
6. Add Component > Search "CommandParser"

**GameManager Configuration:**
- Check "Use Mock LLM" (for testing without API)
- Leave Player Character empty for now (we'll create it next)

### 3. Create Player Character

Create the player avatar:

```
Hierarchy:
└── Player (2D Sprite)
    ├── CharacterStats (Add Component)
    └── PlayerController2D (Add Component)
```

**Steps:**
1. Right-click Hierarchy > 2D Object > Sprites > Square (temporary sprite)
2. Rename to "Player"
3. Set Transform Position to (0, 0, 0)
4. Change Sprite color to Blue (for visibility)
5. Add Component > Search "CharacterStats"
6. Add Component > Search "PlayerController2D"

**CharacterStats Configuration:**
- Character Name: "Hero"
- Race: Human
- Level: 1
- Leave Character Class empty (will be assigned in-game)

**PlayerController2D Configuration:**
- Move Speed: 5
- Use Grid Movement: ✓
- Grid Size: 1

**Link to GameManager:**
1. Select GameSystems
2. Drag Player into "Player Character" field

### 4. Create Chat UI Canvas

Create the chat interface:

```
Hierarchy:
└── Canvas
    ├── ChatPanel (Panel)
    │   ├── ScrollView
    │   │   └── Viewport
    │   │       └── Content (where messages appear)
    │   ├── InputField (TMP)
    │   └── SendButton
    └── ChatUI (Add Component to Canvas)
```

**Steps:**

1. Right-click Hierarchy > UI > Canvas
   - Canvas Scaler: Scale With Screen Size
   - Reference Resolution: 1920x1080

2. Right-click Canvas > UI > Panel
   - Rename to "ChatPanel"
   - Anchor: Bottom
   - Height: 400
   - Color: Semi-transparent black (A: 200)

3. Right-click ChatPanel > UI > Scroll View
   - Remove Horizontal Scrollbar
   - Vertical Scrollbar: Auto-hide
   - Movement Type: Clamped

4. Select Viewport
   - Add Component > Rect Mask 2D

5. Select Content
   - Add Component > Vertical Layout Group
     - Child Alignment: Lower Left
     - Child Force Expand: Width only
     - Spacing: 5
   - Add Component > Content Size Fitter
     - Vertical Fit: Preferred Size

6. Right-click ChatPanel > UI > Input Field (TextMeshPro)
   - Rename to "InputField"
   - Anchor: Bottom
   - Position Y: -350
   - Width: 1600
   - Height: 40
   - Placeholder: "Type your action..."

7. Right-click ChatPanel > UI > Button (TextMeshPro)
   - Rename to "SendButton"
   - Anchor: Bottom Right
   - Position: Next to InputField
   - Width: 100
   - Text: "Send"

8. Select Canvas
   - Add Component > ChatUI
   - Drag references:
     - Scroll Rect: ScrollView
     - Content Panel: Content (inside Viewport)
     - Input Field: InputField
     - Send Button: SendButton

### 5. Create Message Prefabs

Create prefabs for different message types:

**Player Message Prefab:**
1. Right-click Content > UI > Panel
2. Rename to "PlayerMessage"
3. Add Component > Horizontal Layout Group
   - Padding: 10
   - Child Alignment: Middle Right
4. Add Component > Content Size Fitter
   - Vertical Fit: Preferred Size
5. Right-click PlayerMessage > UI > Text (TextMeshPro)
   - Rename to "MessageText"
   - Alignment: Right
   - Color: Light Blue
   - Font Size: 18
6. Drag PlayerMessage to Assets/Prefabs/UI/
7. Delete from Hierarchy

**DM Message Prefab:**
1. Duplicate PlayerMessage prefab
2. Rename to "DMMessage"
3. Edit:
   - Horizontal Layout: Alignment = Middle Left
   - Text: Alignment = Left, Color = White
4. Save

**System Message Prefab:**
1. Duplicate DMMessage prefab
2. Rename to "SystemMessage"
3. Edit:
   - Text: Alignment = Center, Color = Yellow/Gold
   - Font Style: Bold
4. Save

**Link Prefabs to ChatUI:**
1. Select Canvas
2. In ChatUI component:
   - Player Message Prefab: Drag PlayerMessage
   - DM Message Prefab: Drag DMMessage
   - System Message Prefab: Drag SystemMessage

### 6. Create Tilemap (Optional)

Add a basic dungeon map:

```
Hierarchy:
└── Grid
    └── Tilemap
        ├── Tilemap Renderer
        └── Tilemap Collider 2D
```

**Steps:**
1. Right-click Hierarchy > 2D Object > Tilemap > Rectangular
2. Window > 2D > Tile Palette
3. Create New Palette: "DungeonTiles"
4. Import or create tile sprites
5. Paint your dungeon!

**Camera Setup:**
1. Select Main Camera
2. Projection: Orthographic
3. Size: 5 (adjust to fit your view)
4. Background: Black

### 7. Final Configuration

**Event System:**
- Should be auto-created with Canvas
- If not: Right-click Hierarchy > UI > Event System

**GameSystems Final Check:**
1. Select GameSystems
2. Verify all components present:
   - GameManager ✓
   - DungeonMaster ✓
   - CombatManager ✓
   - CommandParser ✓
3. GameManager fields:
   - Player Character: Player ✓
   - Use Mock LLM: ✓ (for testing)

### 8. Test the Scene

Press Play! You should see:
- Chat UI at the bottom
- Welcome message from the system
- Ability to type and send messages
- Player character visible in scene

**Test Commands:**
- Type "start" to begin character creation
- Type "I want to be a fighter"
- Type "attack the goblin" (will create test encounter)

## Creating Character Classes

Create ScriptableObject assets for classes:

1. Right-click Assets/ScriptableObjects/Classes
2. Create > DnD > Character Class
3. Configure:

**Fighter:**
- Class Name: Fighter
- Hit Die Size: 10
- Primary Ability: Strength
- Secondary Ability: Constitution
- Saving Throw Proficiencies: Strength, Constitution
- Armor Proficiencies: Light, Medium, Heavy, Shield
- Weapon Proficiencies: Simple, Martial
- Starting Gold: 150

**Wizard:**
- Class Name: Wizard
- Hit Die Size: 6
- Primary Ability: Intelligence
- Secondary Ability: Wisdom
- Saving Throw Proficiencies: Intelligence, Wisdom
- Is Spellcaster: ✓
- Spellcasting Ability: Intelligence

**Rogue:**
- Class Name: Rogue
- Hit Die Size: 8
- Primary Ability: Dexterity
- Secondary Ability: Intelligence
- Saving Throw Proficiencies: Dexterity, Intelligence

**Cleric:**
- Class Name: Cleric
- Hit Die Size: 8
- Primary Ability: Wisdom
- Secondary Ability: Constitution
- Saving Throw Proficiencies: Wisdom, Charisma
- Is Spellcaster: ✓
- Spellcasting Ability: Wisdom

## Creating Items

Create weapons, armor, and consumables:

**Longsword:**
1. Create > DnD > Items > Weapon
2. Item Name: "Longsword"
3. Damage Dice Count: 1
4. Damage Die: 8
5. Damage Type: Slashing
6. Weapon Type: Martial
7. Value: 15
8. Weight: 3

**Leather Armor:**
1. Create > DnD > Items > Armor
2. Item Name: "Leather Armor"
3. Armor Type: Light
4. Base AC: 11
5. Add Dex Modifier: ✓
6. Value: 10
7. Weight: 10

**Healing Potion:**
1. Create > DnD > Items > Consumable
2. Item Name: "Potion of Healing"
3. Healing Amount: 10
4. Is Stackable: ✓
5. Max Stack Size: 10
6. Value: 50
7. Weight: 0.5

## Troubleshooting

### Chat UI not appearing
- Check Canvas is enabled
- Verify ChatUI component is attached
- Check prefab references are assigned

### Messages not sending
- Verify ChatUI.OnPlayerInput event is subscribed
- Check GameManager.Start() completed
- Look for errors in Console

### Player not moving
- Check PlayerController2D is attached
- Verify Use Grid Movement setting
- Check for Rigidbody2D conflicts

### LLM not responding
- Check "Use Mock LLM" is enabled for testing
- Verify DungeonMaster initialized (check Console logs)
- Look for timeout errors

### Combat not starting
- Verify CombatManager.Instance is not null
- Check player and enemy CharacterStats are valid
- Look for combat state logs in Console

## Next Steps

1. **Test the Game**: Play through character creation and combat
2. **Customize**: Create your own classes, items, and enemies
3. **Integrate Real LLM**: Follow README for OpenAI/LLMUnity setup
4. **Build Dungeon**: Create tilemaps and level designs
5. **Add Features**: Implement spells, quests, NPCs

## Additional Resources

- Unity TextMeshPro: https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/
- Unity Tilemaps: https://docs.unity3d.com/Manual/Tilemap.html
- Unity UI: https://docs.unity3d.com/Packages/com.unity.ugui@1.0/

---

**Scene setup complete!** 🎉

Your D&D game is ready to play. Start by typing in the chat UI and let the AI DM guide your adventure!
