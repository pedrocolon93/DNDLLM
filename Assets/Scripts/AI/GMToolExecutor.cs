using UnityEngine;
using System.Collections.Generic;
using DnD.Character;
using DnD.Core;
using DNDLLM.Map;

namespace DnD.AI
{
    /// <summary>
    /// Parses and executes GM actions. Two paths:
    ///   1. Native function-calls — DM provider returns LLMToolCall objects, dispatched via DispatchToolCall.
    ///   2. Legacy text block — [GM_ACTIONS]…[/GM_ACTIONS] embedded in narration, parsed by ExecuteActions.
    ///
    /// Block format:
    ///   [GM_ACTIONS]
    ///   MOVE player north
    ///   DAMAGE player 5
    ///   HEAL player 3
    ///   ADD_CONDITION player poisoned
    ///   REMOVE_CONDITION player poisoned
    ///   SPAWN_ENEMY Goblin 7 13
    ///   AWARD_XP 50
    ///   [/GM_ACTIONS]
    /// </summary>
    public static class GMToolExecutor
    {
        private static readonly System.Text.RegularExpressions.Regex BlockRegex =
            new System.Text.RegularExpressions.Regex(
                @"\[GM_ACTIONS\](.*?)\[/GM_ACTIONS\]",
                System.Text.RegularExpressions.RegexOptions.Singleline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Strips the GM_ACTIONS block and returns only the narrative text.</summary>
        public static string ExtractNarrative(string fullResponse) =>
            BlockRegex.Replace(fullResponse ?? "", "").Trim();

        /// <summary>
        /// Resolves a tool target string to a specific party member.
        ///
        /// Accepts (case-insensitive):
        ///   "player"     → the current turn owner (passed in as <paramref name="defaultPlayer"/>)
        ///   "player_N"   → 1-based index into the party
        ///   "&lt;name&gt;" → first party member whose characterName contains the string
        ///
        /// Returns null when no match is found AND no default is provided.
        /// </summary>
        public static CharacterStats ResolveTarget(string target, CharacterStats defaultPlayer)
        {
            if (string.IsNullOrEmpty(target)) return defaultPlayer;
            string t = target.Trim().ToLowerInvariant();
            if (t == "player") return defaultPlayer;

            var party = Managers.GameManager.Instance?.Party;
            if (party == null || party.Count == 0) return defaultPlayer;

            // player_N (1-based)
            if (t.StartsWith("player_") && int.TryParse(t.Substring(7), out int idx))
            {
                int zeroIdx = idx - 1;
                if (zeroIdx >= 0 && zeroIdx < party.Count) return party[zeroIdx];
                return defaultPlayer;
            }

            // Name match
            foreach (var p in party)
                if (p != null && !string.IsNullOrEmpty(p.characterName)
                    && p.characterName.ToLowerInvariant().Contains(t))
                    return p;

            return defaultPlayer;
        }

        /// <summary>
        /// Executes every command in the GM_ACTIONS block.
        /// Returns human-readable result lines to display as system messages.
        /// </summary>
        public static List<string> ExecuteActions(string fullResponse, CharacterStats player)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(fullResponse)) return results;

            var match = BlockRegex.Match(fullResponse);
            if (!match.Success) return results;

            foreach (string rawLine in match.Groups[1].Value.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                string[] parts = line.Split(' ');
                if (parts.Length == 0) continue;
                string cmd = parts[0].ToUpper();
                try   { Dispatch(cmd, parts, player, results); }
                catch (System.Exception e)
                { Debug.LogWarning($"[GMToolExecutor] Failed to execute '{line}': {e.Message}"); }
            }
            return results;
        }

        private static void Dispatch(string cmd, string[] p, CharacterStats player, List<string> out_)
        {
            switch (cmd)
            {
                case "MOVE":
                    // MOVE <target> <direction>   (target = "player" | "player_N" | name)
                    if (p.Length >= 3)
                    {
                        var target = ResolveTarget(p[1], player);
                        var ctrl = MapCharacterController.For(target) ?? MapCharacterController.Instance;
                        var (dx, dy) = DirToVector(p[2]);
                        bool ok = ctrl?.TryMove(dx, dy) ?? false;
                        string who = target?.characterName ?? "Player";
                        out_.Add(ok ? $"{who} moves {p[2]}." : $"Something blocks {who}'s path to the {p[2]}.");
                    }
                    break;

                case "DAMAGE":
                    // DAMAGE <target> <amount>
                    if (p.Length >= 3 && int.TryParse(p[2], out int dmg))
                    {
                        var target = ResolveTarget(p[1], player);
                        if (target != null)
                        {
                            target.TakeDamage(dmg);
                            out_.Add($"{target.characterName} takes {dmg} damage! ({target.currentHitPoints}/{target.maxHitPoints} HP)");
                        }
                    }
                    break;

                case "HEAL":
                    // HEAL <target> <amount>
                    if (p.Length >= 3 && int.TryParse(p[2], out int heal))
                    {
                        var target = ResolveTarget(p[1], player);
                        if (target != null)
                        {
                            target.Heal(heal);
                            out_.Add($"{target.characterName} recovers {heal} HP. ({target.currentHitPoints}/{target.maxHitPoints} HP)");
                        }
                    }
                    break;

                case "ADD_CONDITION":
                    // ADD_CONDITION <target> <condition>
                    if (p.Length >= 3 && System.Enum.TryParse<Condition>(p[2], true, out Condition addCond))
                    {
                        var target = ResolveTarget(p[1], player);
                        if (target != null)
                        {
                            target.AddCondition(addCond, 3);
                            out_.Add($"{target.characterName} is now {p[2]}.");
                        }
                    }
                    break;

                case "REMOVE_CONDITION":
                    // REMOVE_CONDITION <target> <condition>
                    if (p.Length >= 3 && System.Enum.TryParse<Condition>(p[2], true, out Condition remCond))
                    {
                        var target = ResolveTarget(p[1], player);
                        if (target != null)
                        {
                            target.RemoveCondition(remCond);
                            out_.Add($"{target.characterName} is no longer {p[2]}.");
                        }
                    }
                    break;

                case "SPAWN_ENEMY":
                    // SPAWN_ENEMY <name> <hp> <ac>
                    if (p.Length >= 4)
                    {
                        int.TryParse(p[2], out int ehp);
                        int.TryParse(p[3], out int eac);
                        SpawnEnemy(p[1], ehp > 0 ? ehp : 10, eac > 0 ? eac : 12);
                        out_.Add($"A {p[1]} appears!");
                    }
                    break;

                case "AWARD_XP":
                    // AWARD_XP <amount>
                    if (p.Length >= 2 && player != null && int.TryParse(p[1], out int xp))
                    {
                        player.currentXP += xp;
                        out_.Add($"You gain {xp} XP.");
                    }
                    break;

                case "KILL_ENTITY":
                    // KILL_ENTITY <name>
                    if (p.Length >= 2)
                    {
                        string targetName = p[1];
                        bool killed = false;
                        for (int i = MapEntityController.All.Count - 1; i >= 0; i--)
                        {
                            var e = MapEntityController.All[i];
                            if (e != null && e.EntityName.ToLower().Contains(targetName.ToLower()))
                            {
                                out_.Add($"The {e.EntityName} falls!");
                                UnityEngine.Object.Destroy(e.gameObject);
                                killed = true;
                                break;
                            }
                        }
                        if (!killed) out_.Add($"No entity named '{targetName}' found.");
                    }
                    break;

                case "REVEAL_ENTITY":
                    // REVEAL_ENTITY <name>  — flips a hidden entity's sprite back on
                    if (p.Length >= 2)
                    {
                        string revealName = p[1];
                        bool revealed = false;
                        foreach (var e in MapEntityController.All)
                        {
                            if (e == null || !e.IsHidden) continue;
                            if (e.EntityName.ToLower().Contains(revealName.ToLower()))
                            {
                                e.IsHidden = false;
                                out_.Add($"The {e.EntityName} steps into view!");
                                revealed = true;
                                break;
                            }
                        }
                        if (!revealed) out_.Add($"Nothing hidden matches '{revealName}'.");
                    }
                    break;

                case "LOCK_DOOR":
                    // LOCK_DOOR <x> <y>
                    if (p.Length >= 3 && int.TryParse(p[1], out int ldx) && int.TryParse(p[2], out int ldy))
                    {
                        var gen = MapGenerator.Instance;
                        if (gen?.grid != null && ldx >= 0 && ldx < gen.width && ldy >= 0 && ldy < gen.height)
                        {
                            gen.grid[ldx, ldy].walkable = false;
                            out_.Add($"The passage at ({ldx},{ldy}) is barred.");
                        }
                    }
                    break;

                case "UNLOCK_DOOR":
                    // UNLOCK_DOOR <x> <y>
                    if (p.Length >= 3 && int.TryParse(p[1], out int udx) && int.TryParse(p[2], out int udy))
                    {
                        var gen = MapGenerator.Instance;
                        if (gen?.grid != null && udx >= 0 && udx < gen.width && udy >= 0 && udy < gen.height)
                        {
                            gen.grid[udx, udy].walkable = true;
                            out_.Add($"The passage at ({udx},{udy}) is now open.");
                        }
                    }
                    break;

                case "ENTER_SUBREGION":
                    // ENTER_SUBREGION <description words…>
                    if (p.Length >= 2)
                    {
                        string regionDesc = string.Join(" ", p, 1, p.Length - 1).Trim();
                        Managers.GameManager.Instance?.RequestSubregionEntry(regionDesc);
                        out_.Add($"Entering: {regionDesc}...");
                    }
                    break;
            }
        }

        // ── Native function-call path ─────────────────────────────────────

        [System.Serializable] private class MoveArgs        { public string target; public string direction; }
        [System.Serializable] private class AmountArgs      { public string target; public int amount; }
        [System.Serializable] private class ConditionArgs   { public string target; public string condition; }
        [System.Serializable] private class SpawnEnemyArgs  { public string name; public int hp; public int ac; }
        [System.Serializable] private class XpArgs          { public int amount; }
        [System.Serializable] private class NameArgs        { public string name; }
        [System.Serializable] private class CoordArgs       { public int x; public int y; }
        [System.Serializable] private class DescArgs        { public string description; }

        private static List<LLMTool> _toolDefs;

        /// <summary>Tool catalogue advertised to the LLM. Schemas mirror the legacy text commands.</summary>
        public static IList<LLMTool> GetToolDefinitions()
        {
            if (_toolDefs != null) return _toolDefs;
            _toolDefs = new List<LLMTool>
            {
                new LLMTool("MOVE",
                    "Move the player one tile in a cardinal direction.",
                    "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"'player' (current turn owner), 'player_N' (1-based party index), or a character name\"},\"direction\":{\"type\":\"string\",\"enum\":[\"north\",\"south\",\"east\",\"west\"]}},\"required\":[\"target\",\"direction\"]}"),

                new LLMTool("DAMAGE",
                    "Deal damage to the player.",
                    "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"'player' (current turn owner), 'player_N' (1-based party index), or a character name\"},\"amount\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"target\",\"amount\"]}"),

                new LLMTool("HEAL",
                    "Heal the player by amount HP.",
                    "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"'player' (current turn owner), 'player_N' (1-based party index), or a character name\"},\"amount\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"target\",\"amount\"]}"),

                new LLMTool("ADD_CONDITION",
                    "Apply a D&D 5e condition to the player.",
                    "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"'player' (current turn owner), 'player_N' (1-based party index), or a character name\"},\"condition\":{\"type\":\"string\"}},\"required\":[\"target\",\"condition\"]}"),

                new LLMTool("REMOVE_CONDITION",
                    "Remove a D&D 5e condition from the player.",
                    "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"'player' (current turn owner), 'player_N' (1-based party index), or a character name\"},\"condition\":{\"type\":\"string\"}},\"required\":[\"target\",\"condition\"]}"),

                new LLMTool("SPAWN_ENEMY",
                    "Spawn a hostile creature and start combat.",
                    "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"hp\":{\"type\":\"integer\",\"minimum\":1},\"ac\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"name\"]}"),

                new LLMTool("AWARD_XP",
                    "Grant experience points to the player.",
                    "{\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"amount\"]}"),

                new LLMTool("KILL_ENTITY",
                    "Remove a named entity from the map.",
                    "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}"),

                new LLMTool("REVEAL_ENTITY",
                    "Reveal a previously hidden entity (the sprite was suppressed at spawn). Use when the player discovers an ambush, sees through a disguise, or otherwise notices something concealed.",
                    "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}"),

                new LLMTool("LOCK_DOOR",
                    "Make the tile at (x,y) impassable.",
                    "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"}},\"required\":[\"x\",\"y\"]}"),

                new LLMTool("UNLOCK_DOOR",
                    "Make the tile at (x,y) passable.",
                    "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"}},\"required\":[\"x\",\"y\"]}"),

                new LLMTool("ENTER_SUBREGION",
                    "Generate a child map and transition the player into it.",
                    "{\"type\":\"object\",\"properties\":{\"description\":{\"type\":\"string\"}},\"required\":[\"description\"]}"),
            };
            return _toolDefs;
        }

        /// <summary>
        /// Execute a single tool call from the LLM and return a short result string suitable for
        /// feeding back as a "tool" role message in the next round.
        /// </summary>
        public static string DispatchToolCall(string toolName, string argsJson, CharacterStats player)
        {
            try
            {
                switch ((toolName ?? "").ToUpperInvariant())
                {
                    case "MOVE":
                    {
                        var a = JsonUtility.FromJson<MoveArgs>(argsJson) ?? new MoveArgs();
                        var (dx, dy) = DirToVector(a.direction ?? "");
                        if (dx == 0 && dy == 0) return $"Unknown direction: {a.direction}";
                        var target = ResolveTarget(a.target, player);
                        var ctrl = MapCharacterController.For(target) ?? MapCharacterController.Instance;
                        bool ok = ctrl?.TryMove(dx, dy) ?? false;
                        string who = target?.characterName ?? "Player";
                        return ok ? $"{who} moved {a.direction}." : $"Move blocked: {who}'s path to {a.direction} is impassable.";
                    }

                    case "DAMAGE":
                    {
                        var a = JsonUtility.FromJson<AmountArgs>(argsJson) ?? new AmountArgs();
                        var target = ResolveTarget(a.target, player);
                        if (target == null) return "No matching character.";
                        target.TakeDamage(a.amount);
                        return $"{target.characterName} took {a.amount} damage. HP {target.currentHitPoints}/{target.maxHitPoints}.";
                    }

                    case "HEAL":
                    {
                        var a = JsonUtility.FromJson<AmountArgs>(argsJson) ?? new AmountArgs();
                        var target = ResolveTarget(a.target, player);
                        if (target == null) return "No matching character.";
                        target.Heal(a.amount);
                        return $"{target.characterName} healed {a.amount} HP. HP {target.currentHitPoints}/{target.maxHitPoints}.";
                    }

                    case "ADD_CONDITION":
                    {
                        var a = JsonUtility.FromJson<ConditionArgs>(argsJson) ?? new ConditionArgs();
                        var target = ResolveTarget(a.target, player);
                        if (target == null) return "No matching character.";
                        if (!System.Enum.TryParse<Condition>(a.condition ?? "", true, out var cond))
                            return $"Unknown condition: {a.condition}.";
                        target.AddCondition(cond, 3);
                        return $"{target.characterName} is now {a.condition}.";
                    }

                    case "REMOVE_CONDITION":
                    {
                        var a = JsonUtility.FromJson<ConditionArgs>(argsJson) ?? new ConditionArgs();
                        var target = ResolveTarget(a.target, player);
                        if (target == null) return "No matching character.";
                        if (!System.Enum.TryParse<Condition>(a.condition ?? "", true, out var cond))
                            return $"Unknown condition: {a.condition}.";
                        target.RemoveCondition(cond);
                        return $"{target.characterName} is no longer {a.condition}.";
                    }

                    case "SPAWN_ENEMY":
                    {
                        var a = JsonUtility.FromJson<SpawnEnemyArgs>(argsJson) ?? new SpawnEnemyArgs();
                        int hp = a.hp > 0 ? a.hp : 10;
                        int ac = a.ac > 0 ? a.ac : 12;
                        SpawnEnemy(a.name ?? "Creature", hp, ac);
                        return $"{a.name ?? "Creature"} (HP {hp}, AC {ac}) spawned. Combat started.";
                    }

                    case "AWARD_XP":
                    {
                        var a = JsonUtility.FromJson<XpArgs>(argsJson) ?? new XpArgs();
                        if (player == null) return "No player character available.";
                        player.currentXP += a.amount;
                        return $"Player gained {a.amount} XP (total {player.currentXP}).";
                    }

                    case "KILL_ENTITY":
                    {
                        var a = JsonUtility.FromJson<NameArgs>(argsJson) ?? new NameArgs();
                        string target = (a.name ?? "").ToLower();
                        for (int i = MapEntityController.All.Count - 1; i >= 0; i--)
                        {
                            var e = MapEntityController.All[i];
                            if (e != null && e.EntityName.ToLower().Contains(target))
                            {
                                Object.Destroy(e.gameObject);
                                return $"{e.EntityName} removed.";
                            }
                        }
                        return $"No entity matching '{a.name}' found.";
                    }

                    case "REVEAL_ENTITY":
                    {
                        var a = JsonUtility.FromJson<NameArgs>(argsJson) ?? new NameArgs();
                        string target = (a.name ?? "").ToLower();
                        foreach (var e in MapEntityController.All)
                        {
                            if (e == null || !e.IsHidden) continue;
                            if (e.EntityName.ToLower().Contains(target))
                            {
                                e.IsHidden = false;
                                return $"{e.EntityName} revealed (sprite now visible).";
                            }
                        }
                        return $"No hidden entity matching '{a.name}' found.";
                    }

                    case "LOCK_DOOR":
                    {
                        var a = JsonUtility.FromJson<CoordArgs>(argsJson) ?? new CoordArgs();
                        var gen = MapGenerator.Instance;
                        if (gen?.grid == null || a.x < 0 || a.x >= gen.width || a.y < 0 || a.y >= gen.height)
                            return $"Invalid coords ({a.x},{a.y}).";
                        gen.grid[a.x, a.y].walkable = false;
                        return $"Tile ({a.x},{a.y}) is now barred.";
                    }

                    case "UNLOCK_DOOR":
                    {
                        var a = JsonUtility.FromJson<CoordArgs>(argsJson) ?? new CoordArgs();
                        var gen = MapGenerator.Instance;
                        if (gen?.grid == null || a.x < 0 || a.x >= gen.width || a.y < 0 || a.y >= gen.height)
                            return $"Invalid coords ({a.x},{a.y}).";
                        gen.grid[a.x, a.y].walkable = true;
                        return $"Tile ({a.x},{a.y}) is now open.";
                    }

                    case "ENTER_SUBREGION":
                    {
                        var a = JsonUtility.FromJson<DescArgs>(argsJson) ?? new DescArgs();
                        Managers.GameManager.Instance?.RequestSubregionEntry(a.description ?? "");
                        return $"Entering subregion: {a.description}.";
                    }

                    default:
                        return $"Unknown tool: {toolName}.";
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GMToolExecutor] Tool '{toolName}' failed: {e.Message}");
                return $"Tool '{toolName}' failed: {e.Message}";
            }
        }

        private static (int dx, int dy) DirToVector(string dir)
        {
            return dir.ToLower() switch
            {
                "north" or "n" => (0,  1),
                "south" or "s" => (0, -1),
                "east"  or "e" => (1,  0),
                "west"  or "w" => (-1, 0),
                _ => (0, 0)
            };
        }

        private static void SpawnEnemy(string name, int hp, int ac)
        {
            var go = new GameObject(name);
            var stats = go.AddComponent<CharacterStats>();
            stats.characterName    = name;
            stats.maxHitPoints     = hp;
            stats.currentHitPoints = hp;
            stats.armorClass       = ac;

            var playerChar = Managers.GameManager.Instance?.GetPlayerCharacter();
            if (playerChar != null)
            {
                Combat.CombatManager.Instance?.StartCombat(
                    new List<CharacterStats> { playerChar },
                    new List<CharacterStats> { stats });
            }
        }
    }
}
