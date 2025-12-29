# D&D LLM - AI-Powered Dungeons & Dragons Game

A 2D Dungeons & Dragons 5e game built in Unity, powered by Large Language Models for dynamic storytelling, natural language commands, and procedurally generated content.

![Unity Version](https://img.shields.io/badge/Unity-2022.3.19f1-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## 🎮 Features

### Core D&D 5e Systems
- **Full D&D 5e Ruleset Implementation**
  - Complete ability score system (STR, DEX, CON, INT, WIS, CHA)
  - Proficiency bonuses and level progression (1-20)
  - Accurate dice rolling with advantage/disadvantage mechanics
  - Critical hits and fumbles
  - Conditions and status effects

- **Turn-Based Combat System**
  - Initiative rolling with DEX modifier tiebreakers
  - Attack rolls vs AC with automatic hit/miss calculation
  - Damage rolls with critical hit support (double dice, not modifiers)
  - State machine-based combat flow
  - Enemy AI with targeting

- **Character Creation & Progression**
  - Multiple character classes (Fighter, Wizard, Rogue, Cleric, etc.)
  - Random ability score generation (4d6 drop lowest) or standard array
  - ScriptableObject-based class architecture
  - XP tracking and automatic leveling
  - Hit point calculation per D&D 5e rules

- **Inventory & Equipment**
  - ScriptableObject-based item system
  - Weapons with damage dice and properties
  - Armor with AC calculations
  - Consumables (potions, scrolls)
  - Weight and value tracking
  - Stackable items support

### AI-Powered Features

- **Natural Language Command Parsing**
  - Type any action in natural language
  - LLM parses input into structured game commands
  - Fallback keyword parsing for offline/mock mode
  - Command pattern for clean execution
  - Supported actions: Attack, Move, Use Item, Rest, Talk, Look, Inventory

- **AI Dungeon Master**
  - Dynamic story generation from campaign prompts
  - Contextual narrative responses to player actions
  - Procedural NPC dialogue
  - Maintains conversation history for coherent storytelling
  - Streaming responses for real-time feel

- **Procedural Content Generation**
  - Creature/enemy stat generation based on challenge rating
  - Story timeline generation from initial premise
  - Dynamic encounter descriptions
  - Environmental narration

- **LLM Provider Architecture**
  - Strategy pattern for swapping LLM providers
  - Support for multiple backends:
    - Mock LLM (for testing without API)
    - OpenAI GPT-4o/GPT-4o Mini (ready to integrate)
    - LLMUnity (local models, ready to integrate)
    - Claude API (ready to integrate)
  - Async/await with cancellation tokens
  - Timeout protection (30-60 seconds)

### UI & Presentation

- **Chat-Based Interface**
  - TextMeshPro for high-quality text rendering
  - Separate message types (Player, DM, System)
  - Typewriter effect for dramatic reveals
  - Auto-scrolling chat window
  - Object pooling for performance
  - Message history limits (configurable)

- **2D Visualization**
  - Grid-based movement system
  - Tilemap support for dungeon layouts
  - Character sprites with SpriteRenderer
  - Visual grid indicators

### Multiplayer Ready

- **Architecture Designed for Networking**
  - Command pattern supports serialization
  - State synchronization points at turn boundaries
  - Player ID-based action validation
  - Ready for Mirror Networking or Unity Netcode integration

## 🏗️ Architecture

### Design Patterns Used

1. **ScriptableObject Pattern**
   - Item definitions (weapons, armor, consumables)
   - Character classes
   - Spell definitions
   - Inventory system

2. **Command Pattern**
   - All player actions encapsulated as commands
   - Supports undo/redo (future)
   - Network-friendly serialization
   - Clean separation of parsing and execution

3. **Strategy Pattern**
   - Swappable LLM providers
   - Runtime provider switching
   - Easy testing with mock implementations

4. **State Machine Pattern**
   - Game states (MainMenu, CharacterCreation, Exploration, Combat)
   - Combat states (Start, RollInitiative, PlayerTurn, EnemyTurn, etc.)
   - Clear state transitions
   - Easy debugging

5. **Singleton Pattern**
   - GameManager
   - DungeonMaster
   - CombatManager
   - ChatUI
   - DontDestroyOnLoad for persistence

6. **Mediator Pattern**
   - GameManager coordinates between systems
   - Event-driven communication
   - Loose coupling between components

### Project Structure

```
Assets/
├── Scripts/
│   ├── Core/              # Core D&D mechanics
│   │   ├── DiceRoller.cs
│   │   ├── DnDEnums.cs
│   │   └── DnDConstants.cs
│   ├── Character/         # Character systems
│   │   ├── AbilityScores.cs
│   │   ├── CharacterClass.cs
│   │   ├── CharacterStats.cs
│   │   └── PlayerController2D.cs
│   ├── Combat/            # Combat systems
│   │   └── CombatManager.cs
│   ├── Inventory/         # Items and inventory
│   │   ├── Item.cs
│   │   └── InventorySystem.cs
│   ├── AI/                # LLM integration
│   │   ├── ILLMProvider.cs
│   │   ├── MockLLMProvider.cs
│   │   ├── GameCommand.cs
│   │   ├── CommandParser.cs
│   │   └── DungeonMaster.cs
│   ├── UI/                # User interface
│   │   └── ChatUI.cs
│   └── Managers/          # Game management
│       └── GameManager.cs
├── Prefabs/               # Reusable prefabs
├── ScriptableObjects/     # Data assets
│   ├── Classes/
│   ├── Items/
│   └── Spells/
├── Scenes/                # Game scenes
├── Sprites/               # 2D artwork
└── Tilemaps/              # Tilemap assets
```

## 🚀 Getting Started

### Prerequisites

- Unity 2022.3.19f1 or later
- Basic understanding of D&D 5e rules (helpful but not required)

### Setup

1. **Clone the repository**
   ```bash
   git clone <your-repo-url>
   cd DNDLLM
   ```

2. **Open in Unity**
   - Open Unity Hub
   - Add project from disk
   - Select the DNDLLM folder
   - Unity will import all assets

3. **Install LLMUnity (Optional - for local LLM)**
   - Download from [LLMUnity GitHub](https://github.com/undreamai/LLMUnity)
   - Or install via Unity Package Manager from git URL:
     ```
     https://github.com/undreamai/LLMUnity.git
     ```

4. **Configure LLM Provider**
   - **Mock Mode (Default)**: Works out of the box, no setup needed
   - **OpenAI**: Create `OpenAIProvider.cs` implementing `ILLMProvider`
   - **LLMUnity**: Uncomment integration code in `GameManager.cs`
   - **Claude**: Create `ClaudeProvider.cs` implementing `ILLMProvider`

### Creating Your First Scene

1. Create a new scene: `File > New Scene > 2D`

2. Add core systems:
   ```
   - Create Empty GameObject: "GameSystems"
   - Add Component: GameManager
   - Add Component: DungeonMaster
   - Add Component: CombatManager
   - Add Component: CommandParser
   ```

3. Create UI Canvas:
   ```
   - Right-click Hierarchy > UI > Canvas
   - Add ChatUI component to Canvas
   - Create UI elements:
     - ScrollRect for chat
     - InputField for player input
     - Button for send
   - Assign references in ChatUI Inspector
   ```

4. Create Player:
   ```
   - Create 2D Sprite GameObject
   - Add CharacterStats component
   - Add PlayerController2D component
   - Assign to GameManager's Player Character field
   ```

5. Press Play!

## 📖 How to Play

### Starting a Campaign

1. **Launch the game** - You'll see the main menu in the chat
2. **Describe your campaign** - Type what kind of adventure you want:
   ```
   "A dark dungeon filled with goblins and treasure"
   "An epic quest to save the kingdom from a dragon"
   "A mystery in a haunted mansion"
   ```
3. **Create your character** - Describe your hero:
   ```
   "I am a brave fighter with a sword and shield"
   "I'm a cunning rogue who specializes in stealth"
   "I want to be a powerful wizard who casts fireballs"
   ```

### Playing the Game

**Natural Language Commands** - Just type what you want to do:
- `"I attack the goblin"` - Engage in combat
- `"Move north"` - Navigate the dungeon
- `"Use a healing potion"` - Consume items
- `"Talk to the merchant"` - Interact with NPCs
- `"Look around"` - Examine surroundings
- `"Check my inventory"` - View items
- `"Rest"` - Recover health

**Keyboard Movement** (Optional):
- WASD or Arrow Keys - Move character on grid
- Space - Interact

### Combat

Combat is turn-based following D&D 5e rules:

1. **Initiative Roll** - All combatants roll D20 + DEX modifier
2. **Turn Order** - Highest to lowest (DEX breaks ties)
3. **Player Turn** - Type your action:
   - `"Attack the orc"`
   - `"Cast magic missile"`
   - `"Drink a potion"`
4. **Enemy Turn** - AI automatically takes actions
5. **Victory** - Gain XP and loot
6. **Defeat** - Game over (with option to retry)

### Example Session

```
[System] Welcome to D&D LLM!
[System] Describe the campaign you'd like to play!

[You] I want to explore an ancient tomb filled with undead creatures

[DM] Your campaign begins in the shadow of an ancient necropolis.
     The stone doors creak open, revealing a dusty corridor ahead...

[System] Tell me about your character!

[You] I'm a holy cleric dedicated to destroying undead

[System] Character Created!
[System] Class: Cleric
[System] HP: 10
[System] AC: 12

[DM] Your divine power radiates as you step into the tomb.
     Skeletal forms begin to stir in the darkness ahead...

[You] I cast turn undead

[DM] Holy light bursts from your symbol! The skeletons recoil...
```

## 🛠️ Configuration

### GameManager Settings

- **Use Mock LLM**: Toggle between mock and real LLM provider
- **Player Character**: Assign the player CharacterStats component
- **Party Members**: Add multiple characters for party-based gameplay

### Combat Settings

- **Initial State**: Combat starting state
- **Turn Timer**: Optional turn time limits

### Chat UI Settings

- **Max Messages**: History limit (default: 100)
- **Auto Scroll**: Keep chat at bottom (default: true)
- **Typewriter Speed**: Character delay for dramatic effect

### LLM Provider Settings

- **Timeout**: Request timeout in seconds (30-60 recommended)
- **Max Context**: Number of messages to maintain in history
- **Streaming**: Enable token-by-token responses

## 🔌 Integrating Real LLM Providers

### OpenAI Integration

```csharp
using OpenAI;

public class OpenAIProvider : ILLMProvider
{
    private OpenAIClient client;

    public async Task InitializeAsync()
    {
        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        client = new OpenAIClient(apiKey);
    }

    public async Task<string> GenerateResponseAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        };

        var response = await client.ChatCompletions.CreateAsync(
            "gpt-4o-mini",
            messages,
            cancellationToken: ct
        );

        return response.Choices[0].Message.Content;
    }
}
```

### LLMUnity Integration

```csharp
using LLMUnity;

public class LLMUnityProvider : ILLMProvider
{
    private LLMCharacter llmCharacter;

    public async Task InitializeAsync()
    {
        llmCharacter = FindObjectOfType<LLMCharacter>();
        await llmCharacter.InitializeAsync();
    }

    public async Task<string> GenerateResponseAsync(string prompt, CancellationToken ct)
    {
        return await llmCharacter.Chat(prompt);
    }
}
```

## 📊 D&D 5e Rules Reference

### Ability Scores & Modifiers

| Score | Modifier | Score | Modifier |
|-------|----------|-------|----------|
| 1     | -5       | 16-17 | +3       |
| 2-3   | -4       | 18-19 | +4       |
| 4-5   | -3       | 20-21 | +5       |
| 6-7   | -2       | 22-23 | +6       |
| 8-9   | -1       | 24-25 | +7       |
| 10-11 | +0       | 26-27 | +8       |
| 12-13 | +1       | 28-29 | +9       |
| 14-15 | +2       | 30    | +10      |

### Proficiency Bonus by Level

| Level | Bonus | Level | Bonus |
|-------|-------|-------|-------|
| 1-4   | +2    | 13-16 | +5    |
| 5-8   | +3    | 17-20 | +6    |
| 9-12  | +4    |       |       |

### Attack Rolls

```
Attack Roll = d20 + Ability Modifier + Proficiency Bonus (if proficient)
Hit if: Attack Roll >= Target's AC
Critical Hit: Natural 20
Critical Miss: Natural 1
```

### Damage Rolls

```
Normal: Weapon Dice + Ability Modifier
Critical: (Weapon Dice × 2) + Ability Modifier
```

## 🎨 Customization

### Creating New Character Classes

1. Right-click in Project: `Create > DnD > Character Class`
2. Configure:
   - Hit Die (d6, d8, d10, d12)
   - Primary/Secondary abilities
   - Proficiencies (armor, weapons, saves)
   - Class features

### Creating New Items

1. **Weapon**: `Create > DnD > Items > Weapon`
2. **Armor**: `Create > DnD > Items > Armor`
3. **Consumable**: `Create > DnD > Items > Consumable`

### Creating New Commands

```csharp
public class CustomCommand : IGameCommand
{
    public string CommandName => "Custom Action";

    public bool CanExecute() { /* validation */ }

    public void Execute() { /* implementation */ }
}
```

## 🧪 Testing

### Mock LLM Mode

The included `MockLLMProvider` allows testing without API costs:
- Keyword-based responses
- Simulates API delays
- Great for development and debugging

### Unit Testing

Key systems are designed for testability:
- Pure functions for dice rolling
- Command pattern allows isolated testing
- Strategy pattern enables mock providers

## 🚀 Performance Optimization

### Implemented Optimizations

- **Object Pooling**: Chat messages reused from pool
- **ScriptableObjects**: Data shared across instances, minimal memory
- **Async/Await**: Non-blocking LLM requests
- **Cancellation Tokens**: Timeout protection prevents hanging
- **Event-Driven**: Components only update when state changes
- **Message History Limits**: Prevents unbounded memory growth

### Tips for Production

1. **Cache LLM Responses**: Store common narrations
2. **Use Local Models**: LLMUnity for offline play
3. **Batch Requests**: Group non-critical requests
4. **Tiered Models**: GPT-4o for story, GPT-4o Mini for simple NPCs
5. **Pre-generate Content**: Create enemies during loading screens

## 📝 License

This project is licensed under the MIT License.

### D&D 5e Content

This project uses the D&D 5e System Reference Document (SRD 5.1) under Creative Commons CC-BY-4.0.

**Attribution**: Wizards of the Coast, D&D 5e SRD

**Note**: This project does NOT include Product Identity such as:
- "Dungeons & Dragons" trademark (referred to as "D&D 5e")
- Specific settings (Forgotten Realms, etc.)
- Unique monsters not in SRD (Mind Flayers, Beholders)

For commercial use, review SRD 5.1 licensing requirements.

## 🤝 Contributing

Contributions welcome! Areas for improvement:

- [ ] Additional character classes (Barbarian, Ranger, Paladin, etc.)
- [ ] Spell system implementation
- [ ] Multiplayer networking (Mirror/Netcode)
- [ ] Image generation for creatures (Stable Diffusion)
- [ ] Advanced AI DM capabilities
- [ ] Quest system
- [ ] Save/Load functionality
- [ ] Character sheet UI
- [ ] Skill checks and ability rolls
- [ ] Rest mechanics (short/long rest)
- [ ] Death saving throws
- [ ] Multi-class support

## 📚 Resources

### D&D 5e
- [SRD 5.1](https://dnd.wizards.com/resources/systems-reference-document)
- [Basic Rules PDF](https://dnd.wizards.com/articles/features/basicrules)

### Unity
- [Unity Manual](https://docs.unity3d.com/Manual/index.html)
- [2D Game Development](https://learn.unity.com/pathway/2d-game-development)

### LLM Integration
- [LLMUnity](https://github.com/undreamai/LLMUnity)
- [OpenAI API](https://platform.openai.com/docs)
- [Anthropic Claude](https://www.anthropic.com/api)

## 🐛 Known Issues

- Mock LLM uses simple keyword matching (replace with real LLM for best experience)
- Combat UI is text-only (visual UI planned)
- Single-player only (multiplayer architecture ready, needs implementation)
- No save/load system yet

## 💬 Support

- **Issues**: [GitHub Issues](https://github.com/pedrocolon93/DNDLLM/issues)
- **Discussions**: [GitHub Discussions](https://github.com/pedrocolon93/DNDLLM/discussions)

---

**Built with ❤️ for the D&D and AI communities**

*Adventure awaits!* 🎲🐉✨
