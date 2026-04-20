using UnityEngine;
using System.Collections.Generic;
using DnD.Character;
using DnD.Core;
using DNDLLM.Map;

namespace DnD.AI
{
    /// <summary>
    /// Parses and executes structured GM action blocks embedded in DM responses.
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
                    // MOVE player <direction>
                    if (p.Length >= 3 && p[1].ToLower() == "player")
                    {
                        var (dx, dy) = DirToVector(p[2]);
                        bool ok = MapCharacterController.Instance?.TryMove(dx, dy) ?? false;
                        out_.Add(ok ? $"You move {p[2]}." : $"Something blocks your path to the {p[2]}.");
                    }
                    break;

                case "DAMAGE":
                    // DAMAGE player <amount>
                    if (p.Length >= 3 && p[1].ToLower() == "player" && player != null
                        && int.TryParse(p[2], out int dmg))
                    {
                        player.TakeDamage(dmg);
                        out_.Add($"You take {dmg} damage! ({player.currentHitPoints}/{player.maxHitPoints} HP)");
                    }
                    break;

                case "HEAL":
                    // HEAL player <amount>
                    if (p.Length >= 3 && p[1].ToLower() == "player" && player != null
                        && int.TryParse(p[2], out int heal))
                    {
                        player.Heal(heal);
                        out_.Add($"You recover {heal} HP. ({player.currentHitPoints}/{player.maxHitPoints} HP)");
                    }
                    break;

                case "ADD_CONDITION":
                    // ADD_CONDITION player <condition>
                    if (p.Length >= 3 && p[1].ToLower() == "player" && player != null
                        && System.Enum.TryParse<Condition>(p[2], true, out Condition addCond))
                    {
                        player.AddCondition(addCond, 3);
                        out_.Add($"You are now {p[2]}.");
                    }
                    break;

                case "REMOVE_CONDITION":
                    // REMOVE_CONDITION player <condition>
                    if (p.Length >= 3 && p[1].ToLower() == "player" && player != null
                        && System.Enum.TryParse<Condition>(p[2], true, out Condition remCond))
                    {
                        player.RemoveCondition(remCond);
                        out_.Add($"You are no longer {p[2]}.");
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
