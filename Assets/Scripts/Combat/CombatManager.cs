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
    /// Manages turn-based D&D 5e combat. Turn order is owned by GameManager.Turns
    /// (TurnQueue); this class only tracks the battle phase (Start/RollInitiative/
    /// PlayerAction/EnemyAction/Won/Lost) and dispatches per-turn behaviour.
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

        private bool playerActedThisTurn = false;
        private string lastAttackSummary = "";

        public System.Action<string> OnCombatMessage;
        public System.Action OnPlayerTurnStart;
        public System.Action OnPlayerTurnEnd;
        public System.Action OnEnemyTurnStart;
        public System.Action OnEnemyTurnEnd;
        public System.Action<bool> OnCombatEnded;

        public void NotifyPlayerActed() => playerActedThisTurn = true;

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

            if (GameManager.Instance != null
                && GameManager.Instance.GetCurrentState() != GameState.Combat)
                GameManager.Instance.ChangeState(GameState.Combat);

            StartCoroutine(CombatFlow());
        }

        private IEnumerator CombatFlow()
        {
            currentState = BattleState.RollInitiative;
            RollInitiativeIntoQueue();
            yield return new WaitForSeconds(1f);

            while (inCombat)
            {
                if (enemies.All(e => e.currentHitPoints <= 0))
                {
                    currentState = BattleState.BattleWon;
                    OnCombatMessage?.Invoke("Victory! All enemies defeated!");
                    EndCombat(true);
                    yield break;
                }

                // 5e: party is lost only when every member is dead (failed all death saves).
                if (playerCharacters.All(p => p.isDead || (p.currentHitPoints <= 0 && p.deathSaveFailures >= 3)))
                {
                    currentState = BattleState.BattleLost;
                    OnCombatMessage?.Invoke("Defeat! The party has fallen.");
                    EndCombat(false);
                    yield break;
                }

                yield return StartCoroutine(ExecuteTurn());

                var gm = GameManager.Instance;
                if (gm != null) gm.Turns.EndTurn();

                if (gm != null && gm.Turns.CurrentIndex == 0)
                    UpdateAllConditions();
            }
        }

        private void RollInitiativeIntoQueue()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // TurnQueue handles the roll + sort; we just announce the results from the resulting order.
            gm.Turns.BeginCombatRound(playerCharacters, enemies);
            foreach (var entry in gm.Turns.Order)
                if (entry != null && entry.Stats != null)
                    OnCombatMessage?.Invoke($"{entry.Stats.characterName} rolled {entry.Initiative} for initiative!");
        }

        private IEnumerator ExecuteTurn()
        {
            var gm = GameManager.Instance;
            if (gm == null) { yield break; }
            var character = gm.Turns.CurrentActor;
            if (character == null) { yield break; }

            bool isPlayerCombatant = gm.Turns.IsCurrentActorPlayer;

            if (character.currentHitPoints <= 0)
            {
                // Player characters at 0HP roll death saves each turn; enemies just stay down.
                if (isPlayerCombatant && !character.isDead && !character.isStable)
                {
                    var (_, msg) = character.RollDeathSave();
                    OnCombatMessage?.Invoke(msg);
                    if (character.isDead) OnCombatMessage?.Invoke($"{character.characterName} has died.");
                }
                yield break;
            }

            currentState = isPlayerCombatant ? BattleState.PlayerTurn : BattleState.EnemyTurn;
            OnCombatMessage?.Invoke($"--- {character.characterName}'s Turn ---");

            if (isPlayerCombatant)
            {
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

            if (DungeonMaster.Instance != null)
            {
                yield return ExecuteEnemyTurnViaDM(enemy);
                yield break;
            }

            // Fallback: simple AI — attack a random alive player using the dice formula.
            var alivePlayerTargets = playerCharacters.Where(p => p.currentHitPoints > 0).ToList();
            if (alivePlayerTargets.Count > 0)
            {
                var target = alivePlayerTargets[Random.Range(0, alivePlayerTargets.Count)];
                lastAttackSummary = "";
                ExecuteAttack(enemy, target);
            }
        }

        private IEnumerator ExecuteEnemyTurnViaDM(CharacterStats enemy)
        {
            var alive = playerCharacters.Where(p => p != null && p.currentHitPoints > 0)
                                        .Select(p => p.characterName).ToList();
            string targets = alive.Count > 0 ? string.Join(", ", alive) : "no one";

            string action  = $"It is the enemy's turn. The {enemy.characterName} acts now " +
                             $"(HP {enemy.currentHitPoints}/{enemy.maxHitPoints}, AC {enemy.armorClass}). " +
                             $"Possible targets: {targets}. Take exactly one short hostile action — call DAMAGE " +
                             $"on a target, then end with a 1-2 sentence narration. Do NOT call MOVE for the player.";
            string ctx     = "Combat — DM controls the enemy. Call exactly one DAMAGE or condition tool, then narrate.";
            var    runTask = DungeonMaster.Instance.RunPlayerTurnAsync(action, ctx, enemy, maxToolSteps: 4);

            while (!runTask.IsCompleted) yield return null;
            string narration = "";
            try { narration = runTask.Result ?? ""; }
            catch (System.Exception e) { Debug.LogWarning($"[CombatManager] DM enemy turn failed: {e.Message}"); }

            if (!string.IsNullOrEmpty(narration))
                OnCombatMessage?.Invoke($"{enemy.characterName} acts.");
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
            var gm = GameManager.Instance;
            if (gm == null) return;
            foreach (var entry in gm.Turns.Order)
                if (entry != null && entry.Stats != null) entry.Stats.UpdateConditionDurations();
        }

        private void EndCombat(bool victory)
        {
            inCombat = false;

            if (victory)
            {
                int totalXP = enemies.Count * 50;
                foreach (var player in playerCharacters)
                    player.GainXP(totalXP / playerCharacters.Count);
            }

            playerActedThisTurn = true; // wake any pending WaitUntil so the coroutine can exit
            OnCombatEnded?.Invoke(victory);

            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Turns.BeginExplorationRound(gm.Party);
                if (victory && gm.GetCurrentState() == GameState.Combat)
                    gm.ChangeState(GameState.Exploration);
            }
        }

        public CharacterStats GetCurrentTurnCharacter()
        {
            return GameManager.Instance?.Turns.CurrentActor;
        }

        public bool IsPlayerTurn()
        {
            return currentState == BattleState.PlayerTurn || currentState == BattleState.PlayerAction;
        }
    }
}
