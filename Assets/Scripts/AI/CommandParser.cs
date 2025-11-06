using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using DnD.Character;

namespace DnD.AI
{
    /// <summary>
    /// Parses natural language input into game commands using LLM
    /// </summary>
    public class CommandParser : MonoBehaviour
    {
        [Header("LLM Configuration")]
        [SerializeField] private bool useLLMParsing = true;

        private ILLMProvider llmProvider;

        private const string SYSTEM_PROMPT = @"You are a command parser for a D&D game. Parse player input into structured commands.
Available commands:
- ATTACK [target] - Attack an enemy
- MOVE [direction] - Move in a direction (north/south/east/west/forward/back/left/right)
- USE [item] - Use an item from inventory
- REST - Take a short rest
- TALK [npc] - Talk to an NPC
- LOOK - Examine surroundings
- INVENTORY - Check inventory

Respond with ONLY the command in this format:
COMMAND: [command_name]
TARGET: [target if applicable]
DETAILS: [any additional details]

Example inputs and outputs:
Input: 'I want to attack the goblin'
Output:
COMMAND: ATTACK
TARGET: goblin

Input: 'move north'
Output:
COMMAND: MOVE
TARGET: north

Input: 'drink a health potion'
Output:
COMMAND: USE
TARGET: health potion";

        public void Initialize(ILLMProvider provider)
        {
            llmProvider = provider;
        }

        public async Task<IGameCommand> ParseCommandAsync(string naturalLanguageInput, CharacterStats playerCharacter)
        {
            if (string.IsNullOrWhiteSpace(naturalLanguageInput))
                return null;

            string commandText;

            if (useLLMParsing && llmProvider != null && llmProvider.IsReady)
            {
                // Use LLM to parse command
                try
                {
                    CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    commandText = await llmProvider.GenerateResponseAsync(SYSTEM_PROMPT, naturalLanguageInput, cts.Token);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"LLM parsing failed: {e.Message}. Falling back to keyword parsing.");
                    commandText = KeywordParse(naturalLanguageInput);
                }
            }
            else
            {
                // Fallback to simple keyword parsing
                commandText = KeywordParse(naturalLanguageInput);
            }

            return CreateCommandFromParsedText(commandText, playerCharacter);
        }

        private string KeywordParse(string input)
        {
            string lower = input.ToLower();

            if (Regex.IsMatch(lower, @"\battack\b|\bfight\b|\bhit\b|\bstrike\b"))
            {
                string target = ExtractTarget(input, new[] { "goblin", "orc", "dragon", "enemy" });
                return $"COMMAND: ATTACK\nTARGET: {target}";
            }

            if (Regex.IsMatch(lower, @"\bmove\b|\bgo\b|\bwalk\b"))
            {
                string direction = ExtractDirection(input);
                return $"COMMAND: MOVE\nTARGET: {direction}";
            }

            if (Regex.IsMatch(lower, @"\buse\b|\bdrink\b|\beat\b|\bconsume\b"))
            {
                string item = ExtractTarget(input, new[] { "potion", "scroll", "food" });
                return $"COMMAND: USE\nTARGET: {item}";
            }

            if (Regex.IsMatch(lower, @"\brest\b|\bsleep\b"))
            {
                return "COMMAND: REST";
            }

            if (Regex.IsMatch(lower, @"\btalk\b|\bspeak\b|\bchat\b"))
            {
                string npc = ExtractTarget(input, new[] { "guard", "merchant", "wizard", "npc" });
                return $"COMMAND: TALK\nTARGET: {npc}";
            }

            if (Regex.IsMatch(lower, @"\blook\b|\bexamine\b|\bsearch\b"))
            {
                return "COMMAND: LOOK";
            }

            if (Regex.IsMatch(lower, @"\binventory\b|\bitems\b|\bbackpack\b"))
            {
                return "COMMAND: INVENTORY";
            }

            return "COMMAND: UNKNOWN";
        }

        private string ExtractTarget(string input, string[] possibleTargets)
        {
            string lower = input.ToLower();
            foreach (string target in possibleTargets)
            {
                if (lower.Contains(target))
                    return target;
            }
            return "unknown";
        }

        private string ExtractDirection(string input)
        {
            string lower = input.ToLower();
            if (lower.Contains("north") || lower.Contains("up")) return "north";
            if (lower.Contains("south") || lower.Contains("down")) return "south";
            if (lower.Contains("east") || lower.Contains("right")) return "east";
            if (lower.Contains("west") || lower.Contains("left")) return "west";
            if (lower.Contains("forward")) return "forward";
            if (lower.Contains("back")) return "back";
            return "forward";
        }

        private IGameCommand CreateCommandFromParsedText(string parsedText, CharacterStats playerCharacter)
        {
            string command = ExtractValue(parsedText, "COMMAND");
            string target = ExtractValue(parsedText, "TARGET");

            switch (command.ToUpper())
            {
                case "ATTACK":
                    // TODO: Find actual target by name
                    return new AttackCommand(playerCharacter, null);

                case "MOVE":
                    Vector2 direction = DirectionToVector(target);
                    return new MoveCommand(playerCharacter.transform, direction);

                case "USE":
                    return new UseItemCommand(playerCharacter, target);

                case "REST":
                    return new RestCommand(playerCharacter);

                case "TALK":
                    return new DialogueCommand(target, "");

                default:
                    Debug.LogWarning($"Unknown command: {command}");
                    return null;
            }
        }

        private string ExtractValue(string text, string key)
        {
            var match = Regex.Match(text, $@"{key}:\s*(.+?)(?:\n|$)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private Vector2 DirectionToVector(string direction)
        {
            switch (direction.ToLower())
            {
                case "north": case "up": return Vector2.up;
                case "south": case "down": return Vector2.down;
                case "east": case "right": return Vector2.right;
                case "west": case "left": return Vector2.left;
                case "forward": return Vector2.up;
                case "back": return Vector2.down;
                default: return Vector2.zero;
            }
        }
    }
}
