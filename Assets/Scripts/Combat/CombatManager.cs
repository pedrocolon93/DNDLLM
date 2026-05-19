using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DnD.Core;
using DnD.Character;
using DnD.AI;
using DnD.Managers;

namespace DnD.Combat
{
    /// <summary>
    /// Manages turn-based D&D 5e combat using state machine pattern
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        public static CombatManager Instance { get; private set; }

        [Header("Combat State")]
        public BattleState currentState = BattleState.Start;
        public bool inCombat = false;

        [Header("Combatants")]
        public List<CharacterStats> playerCharacters = new List<CharacterStats>();
        public List<CharacterStats> enemies = new List<CharacterStats>();

        private List<CombatantInitiative> initiativeOrder = new List<CombatantInitiative>();
        private int currentTurnIndex = 0;
        private bool playerActedThisTurn = false;
        private string lastAttackSummary = "";

        public System.Action<string> OnCombatMessage;
        public System.Action OnPlayerTurnStart;
        public System.Action OnPlayerTurnEnd;
        public System.Action OnEnemyTurnStart;
        public System.Action OnEnemyTurnEnd;
        public System.Action<bool> OnCombatEnded; // true if victory

        /// <summary>Called by the player-input handler once a combat command has been dispatched.</summary>
        public void NotifyPlayerActed() => playerActedThisTurn = true;

        private class CombatantInitiative
        {
            public CharacterStats character;
            public int initiative;

            public CombatantInitiative(CharacterStats character, int initiative)
            {
                this.character = character;
                this.initiative = initiative;
            }
        }

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

        public void StartCombat(List<CharacterStats> players, List<CharacterStats> enemyList)
        {
            playerCharacters = players;
            enemies = enemyList;
            inCombat = true;
            currentState = BattleState.Start;

            // Switch GameManager into Combat state so the HUD + chat reflect the new mode.
            if (Managers.GameManager.Instance != null
                && Managers.GameManager.Instance.GetCurrentState() != GameState.Combat)
                Managers.GameManager.Instance.ChangeState(GameState.Combat);

            StartCoroutine(CombatFlow());
        }

        private IEnumerator CombatFlow()
        {
            // Roll initiative
            currentState = BattleState.RollInitiative;
            RollInitiative();
            yield return new WaitForSeconds(1f);

            // Combat loop
            while (inCombat)
            {
                // Check win/loss conditions
                if (enemies.All(e => e.currentHitPoints <= 0))
                {
                    currentState = BattleState.BattleWon;
                    OnCombatMessage?.Invoke("Victory! All enemies defeated!");
                    EndCombat(true);
                    yield break;
                }

                if (playerCharacters.All(p => p.currentHitPoints <= 0))
                {
                    currentState = BattleState.BattleLost;
                    OnCombatMessage?.Invoke("Defeat! All party members are unconscious!");
                    EndCombat(false);
                    yield break;
                }

                // Execute turn
                SyncTurnQueueIndex();
                yield return StartCoroutine(ExecuteTurn());

                // Advance to next combatant
                currentTurnIndex = (currentTurnIndex + 1) % initiativeOrder.Count;
                SyncTurnQueueIndex();

                // Update conditions at end of round
                if (currentTurnIndex == 0)
                {
                    UpdateAllConditions();
                }
            }
        }

        private void RollInitiative()
        {
            initiativeOrder.Clear();

            foreach (var player in playerCharacters)
            {
                int init = player.RollInitiative();
                initiativeOrder.Add(new CombatantInitiative(player, init));
                OnCombatMessage?.Invoke($"{player.characterName} rolled {init} for initiative!");
            }

            foreach (var enemy in enemies)
            {
                int init = enemy.RollInitiative();
                initiativeOrder.Add(new CombatantInitiative(enemy, init));
                OnCombatMessage?.Invoke($"{enemy.characterName} rolled {init} for initiative!");
            }

            // Sort by initiative (highest first), break ties with DEX
            initiativeOrder = initiativeOrder.OrderByDescending(c => c.initiative)
                .ThenByDescending(c => c.character.abilities.GetModifier(AbilityScore.Dexterity))
                .ToList();

            currentTurnIndex = 0;

            // Mirror the combat order into the global TurnQueue so the HUD strip shows
            // who's up next during combat too. The queue is a view of the same logical
            // order; CombatManager remains the source of truth for actual turn execution.
            var gm = GameManager.Instance;
            if (gm != null)
            {
                var orderedChars = initiativeOrder.Select(c => c.character).ToList();
                gm.Turns.BeginCombat(orderedChars,
                    c => playerCharacters.Contains(c));
            }
        }

        /// <summary>Sync TurnQueue.CurrentIndex with combat's currentTurnIndex so the HUD strip
        /// highlights the active combatant. Called every time we advance.</summary>
        private void SyncTurnQueueIndex()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Turns.Count == 0) return;
            // The TurnQueue exposes Compact() / AdvanceTurn(); we mirror by advancing N times
            // from index 0. Since combat rebuilds the queue at the start, the offsets line up.
            // Cheaper than adding an explicit "SetCurrent" API.
            while (gm.Turns.CurrentIndex != currentTurnIndex && gm.Turns.Count > 0)
            {
                int before = gm.Turns.CurrentIndex;
                gm.Turns.AdvanceTurn();
                if (gm.Turns.CurrentIndex == before) break; // safety: don't loop forever
            }
        }

        private IEnumerator ExecuteTurn()
        {
            var currentCombatant = initiativeOrder[currentTurnIndex];
            var character = currentCombatant.character;

            if (character.currentHitPoints <= 0)
            {
                yield break; // Skip turn if unconscious
            }

            bool isPlayer = playerCharacters.Contains(character);
            currentState = isPlayer ? BattleState.PlayerTurn : BattleState.EnemyTurn;

            OnCombatMessage?.Invoke($"--- {character.characterName}'s Turn ---");

            if (isPlayer)
            {
                // Wait for the player to actually issue a combat command via the chat → DM pipeline.
                currentState = BattleState.PlayerAction;
                playerActedThisTurn = false;
                OnPlayerTurnStart?.Invoke();
                yield return new WaitUntil(() => playerActedThisTurn || !inCombat);
                OnPlayerTurnEnd?.Invoke();
            }
            else
            {
                currentState = BattleState.EnemyAction;
                OnEnemyTurnStart?.Invoke();
                yield return StartCoroutine(ExecuteEnemyTurn(character));
                OnEnemyTurnEnd?.Invoke();
            }
        }

        private IEnumerator ExecuteEnemyTurn(CharacterStats enemy)
        {
            yield return new WaitForSeconds(0.5f);

            // Simple AI: attack random player
            var alivePlayerTargets = playerCharacters.Where(p => p.currentHitPoints > 0).ToList();
            if (alivePlayerTargets.Count > 0)
            {
                var target = alivePlayerTargets[Random.Range(0, alivePlayerTargets.Count)];
                lastAttackSummary = "";
                ExecuteAttack(enemy, target);

                // Hand the mechanical summary off to the DM for in-character narration.
                // Fire-and-forget — combat flow continues regardless of the LLM's latency.
                if (DungeonMaster.Instance != null && !string.IsNullOrEmpty(lastAttackSummary))
                {
                    string summary = lastAttackSummary;
                    string actor = enemy.characterName;
                    string victim = target.characterName;
                    _ = DungeonMaster.Instance.NarrateActionAsync(
                        $"In combat: {actor} attacked {victim}. {summary}",
                        "Combat narration — describe what just happened in 1-2 vivid sentences.");
                }
            }
        }

        public void ExecuteAttack(CharacterStats attacker, CharacterStats target, int weaponDamage = 4, int weaponDamageDie = 6)
        {
            int attackRoll = DiceRoller.D20Roll();
            int attackBonus = attacker.abilities.GetModifier(AbilityScore.Strength) + attacker.GetProficiencyBonus();
            int totalAttack = attackRoll + attackBonus;

            OnCombatMessage?.Invoke($"{attacker.characterName} attacks {target.characterName}!");
            OnCombatMessage?.Invoke($"Attack roll: {attackRoll} + {attackBonus} = {totalAttack} vs AC {target.armorClass}");

            if (attackRoll == 20)
            {
                int damage = DiceRoller.RollDamage(weaponDamage, weaponDamageDie,
                    attacker.abilities.GetModifier(AbilityScore.Strength), isCritical: true);
                target.TakeDamage(damage);
                lastAttackSummary = $"Critical hit for {damage} damage. {target.characterName} HP {target.currentHitPoints}/{target.maxHitPoints}.";
                OnCombatMessage?.Invoke($"CRITICAL HIT! {damage} damage dealt!");
            }
            else if (attackRoll == 1)
            {
                lastAttackSummary = $"Critical miss — {attacker.characterName} fumbled.";
                OnCombatMessage?.Invoke($"Critical miss!");
            }
            else if (totalAttack >= target.armorClass)
            {
                int damage = DiceRoller.RollDamage(weaponDamage, weaponDamageDie,
                    attacker.abilities.GetModifier(AbilityScore.Strength));
                target.TakeDamage(damage);
                lastAttackSummary = $"Hit for {damage} damage. {target.characterName} HP {target.currentHitPoints}/{target.maxHitPoints}.";
                OnCombatMessage?.Invoke($"Hit! {damage} damage dealt!");
            }
            else
            {
                lastAttackSummary = $"Miss — attack roll {totalAttack} vs AC {target.armorClass}.";
                OnCombatMessage?.Invoke($"Miss!");
            }

            OnCombatMessage?.Invoke($"{target.characterName} HP: {target.currentHitPoints}/{target.maxHitPoints}");
        }

        private void UpdateAllConditions()
        {
            foreach (var combatant in initiativeOrder)
            {
                combatant.character.UpdateConditionDurations();
            }
        }

        private void EndCombat(bool victory)
        {
            inCombat = false;

            if (victory)
            {
                // Award XP
                int totalXP = enemies.Count * 50; // Simple XP calculation
                foreach (var player in playerCharacters)
                {
                    player.GainXP(totalXP / playerCharacters.Count);
                }
            }

            initiativeOrder.Clear();
            // Wake the player-turn WaitUntil so the coroutine can exit cleanly.
            playerActedThisTurn = true;
            OnCombatEnded?.Invoke(victory);

            // Restore exploration: rebuild the TurnQueue with the party only,
            // and flip GameManager back to Exploration so chat input is gated correctly.
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Turns.BeginExploration(gm.Party);
                if (victory && gm.GetCurrentState() == GameState.Combat)
                    gm.ChangeState(GameState.Exploration);
            }
        }

        public CharacterStats GetCurrentTurnCharacter()
        {
            if (initiativeOrder.Count > 0)
                return initiativeOrder[currentTurnIndex].character;
            return null;
        }

        public bool IsPlayerTurn()
        {
            return currentState == BattleState.PlayerTurn || currentState == BattleState.PlayerAction;
        }
    }
}
