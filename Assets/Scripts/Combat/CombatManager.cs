using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DnD.Core;
using DnD.Character;

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

        public System.Action<string> OnCombatMessage;

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
                yield return StartCoroutine(ExecuteTurn());

                // Advance to next combatant
                currentTurnIndex = (currentTurnIndex + 1) % initiativeOrder.Count;

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
                // Wait for player action (handled by UI/Input)
                currentState = BattleState.PlayerAction;
                // This would normally wait for player input through events
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                // AI turn
                currentState = BattleState.EnemyAction;
                yield return StartCoroutine(ExecuteEnemyTurn(character));
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
                ExecuteAttack(enemy, target);
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
                // Critical hit!
                int damage = DiceRoller.RollDamage(weaponDamage, weaponDamageDie,
                    attacker.abilities.GetModifier(AbilityScore.Strength), isCritical: true);
                target.TakeDamage(damage);
                OnCombatMessage?.Invoke($"CRITICAL HIT! {damage} damage dealt!");
            }
            else if (attackRoll == 1)
            {
                OnCombatMessage?.Invoke($"Critical miss!");
            }
            else if (totalAttack >= target.armorClass)
            {
                int damage = DiceRoller.RollDamage(weaponDamage, weaponDamageDie,
                    attacker.abilities.GetModifier(AbilityScore.Strength));
                target.TakeDamage(damage);
                OnCombatMessage?.Invoke($"Hit! {damage} damage dealt!");
            }
            else
            {
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
