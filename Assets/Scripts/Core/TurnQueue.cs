using System;
using System.Collections.Generic;
using System.Linq;
using DnD.Character;
using DnD.Core;

namespace DnD.Core
{
    /// <summary>
    /// Single source of truth for "whose turn is it". Used by exploration
    /// (party-only round-robin) and combat (initiative-sorted party+enemies).
    /// </summary>
    public class TurnQueue
    {
        public sealed class Entry
        {
            public CharacterStats Stats;
            public bool           IsPlayer;
            public string         DisplayName;
            public int            Initiative;
        }

        private readonly List<Entry> _order = new List<Entry>();
        private int _currentIndex;
        private bool _isCombat;

        public event Action OnTurnChanged;
        public event Action<CharacterStats> OnActorChanged;

        public IReadOnlyList<Entry> Order => _order;
        public int CurrentIndex => _currentIndex;
        public int Count => _order.Count;
        public bool IsCombat => _isCombat;

        public Entry Current =>
            (_order.Count == 0 || _currentIndex < 0 || _currentIndex >= _order.Count) ? null : _order[_currentIndex];

        public CharacterStats CurrentActor => Current?.Stats;
        public bool IsCurrentActorPlayer => Current != null && Current.IsPlayer;
        public bool IsPlayerTurn => IsCurrentActorPlayer;

        public void BeginExplorationRound(IEnumerable<CharacterStats> party)
        {
            _isCombat = false;
            _order.Clear();
            if (party != null)
                foreach (var p in party)
                    if (p != null) _order.Add(new Entry { Stats = p, IsPlayer = true, DisplayName = p.characterName, Initiative = 0 });
            _currentIndex = 0;
            FireChanged();
        }

        public void BeginCombatRound(IEnumerable<CharacterStats> party, IEnumerable<CharacterStats> enemies)
        {
            _isCombat = true;
            _order.Clear();
            var partyList   = party   != null ? party.Where(p   => p   != null).ToList() : new List<CharacterStats>();
            var enemyList   = enemies != null ? enemies.Where(e => e != null).ToList() : new List<CharacterStats>();

            var rolled = new List<Entry>(partyList.Count + enemyList.Count);
            foreach (var p in partyList)
                rolled.Add(new Entry { Stats = p, IsPlayer = true,  DisplayName = p.characterName, Initiative = p.RollInitiative() });
            foreach (var e in enemyList)
                rolled.Add(new Entry { Stats = e, IsPlayer = false, DisplayName = e.characterName, Initiative = e.RollInitiative() });

            // Highest initiative first; ties broken by Dex modifier desc.
            rolled.Sort((a, b) =>
            {
                int cmp = b.Initiative.CompareTo(a.Initiative);
                if (cmp != 0) return cmp;
                int aDex = a.Stats != null ? a.Stats.abilities.GetModifier(AbilityScore.Dexterity) : 0;
                int bDex = b.Stats != null ? b.Stats.abilities.GetModifier(AbilityScore.Dexterity) : 0;
                return bDex.CompareTo(aDex);
            });

            _order.AddRange(rolled);
            _currentIndex = 0;
            FireChanged();
        }

        // Back-compat shim used by save/load and a few callers that prebuild order externally.
        public void BeginExploration(IEnumerable<CharacterStats> players) => BeginExplorationRound(players);

        // Back-compat shim: caller supplies an already-ordered combatant list.
        public void BeginCombat(IEnumerable<CharacterStats> orderedCombatants, Func<CharacterStats, bool> isPlayer)
        {
            _isCombat = true;
            _order.Clear();
            if (orderedCombatants != null)
                foreach (var c in orderedCombatants)
                    if (c != null) _order.Add(new Entry
                    {
                        Stats       = c,
                        IsPlayer    = isPlayer != null && isPlayer(c),
                        DisplayName = c.characterName,
                        Initiative  = c.initiative,
                    });
            _currentIndex = 0;
            FireChanged();
        }

        public void EndTurn() => AdvanceTurn();

        public void AdvanceTurn()
        {
            if (_order.Count == 0) return;
            for (int i = 0; i < _order.Count; i++)
            {
                _currentIndex = (_currentIndex + 1) % _order.Count;
                var e = _order[_currentIndex];
                if (e != null && e.Stats != null) { FireChanged(); return; }
            }
            _order.Clear();
            _currentIndex = 0;
            FireChanged();
        }

        public void Clear()
        {
            _isCombat = false;
            _order.Clear();
            _currentIndex = 0;
            FireChanged();
        }

        public void Compact()
        {
            int removed = _order.RemoveAll(e => e == null || e.Stats == null);
            if (removed == 0) return;
            if (_currentIndex >= _order.Count) _currentIndex = 0;
            FireChanged();
        }

        private void FireChanged()
        {
            OnTurnChanged?.Invoke();
            OnActorChanged?.Invoke(CurrentActor);
        }
    }
}
