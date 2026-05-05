using System;
using System.Collections.Generic;
using DnD.Character;

namespace DnD.Core
{
    /// <summary>
    /// Ordered list of who acts next. Drives both exploration and combat — exploration
    /// rotates through party members one input at a time; combat extends the queue with
    /// enemies in initiative order.
    ///
    /// The queue itself is provider-agnostic: it doesn't care whether the entry is a
    /// human player or an AI-driven NPC. <see cref="IsPlayerTurn"/> tells the caller
    /// whether the chat input should accept commands this round.
    /// </summary>
    public class TurnQueue
    {
        public sealed class Entry
        {
            public CharacterStats Stats;
            public bool           IsPlayer;     // false = enemy/NPC turn — input gated off
            public string         DisplayName;  // cached so the HUD strip never dereferences a destroyed CharacterStats
        }

        private readonly List<Entry> _order = new List<Entry>();
        private int _currentIndex;

        /// <summary>Fired whenever the active turn changes (queue rebuilt or AdvanceTurn called).</summary>
        public event Action OnTurnChanged;

        public IReadOnlyList<Entry> Order => _order;
        public int CurrentIndex => _currentIndex;
        public int Count => _order.Count;

        public Entry Current =>
            (_order.Count == 0 || _currentIndex < 0 || _currentIndex >= _order.Count) ? null : _order[_currentIndex];

        public bool IsPlayerTurn => Current != null && Current.IsPlayer;

        /// <summary>Replace the entire queue with a single rotation over the party (no enemies).</summary>
        public void BeginExploration(IEnumerable<CharacterStats> players)
        {
            _order.Clear();
            if (players != null)
                foreach (var p in players)
                    if (p != null) _order.Add(new Entry { Stats = p, IsPlayer = true, DisplayName = p.characterName });
            _currentIndex = 0;
            OnTurnChanged?.Invoke();
        }

        /// <summary>Replace the queue with players + enemies for a combat encounter.
        /// Caller is responsible for the desired order (e.g. by initiative roll).</summary>
        public void BeginCombat(IEnumerable<CharacterStats> orderedCombatants, Func<CharacterStats, bool> isPlayer)
        {
            _order.Clear();
            if (orderedCombatants != null)
                foreach (var c in orderedCombatants)
                    if (c != null) _order.Add(new Entry
                    {
                        Stats       = c,
                        IsPlayer    = isPlayer != null && isPlayer(c),
                        DisplayName = c.characterName,
                    });
            _currentIndex = 0;
            OnTurnChanged?.Invoke();
        }

        /// <summary>Advance to the next entry, wrapping around. Skips entries whose
        /// CharacterStats has been destroyed (defeated and Destroy()'d).</summary>
        public void AdvanceTurn()
        {
            if (_order.Count == 0) return;
            for (int i = 0; i < _order.Count; i++)
            {
                _currentIndex = (_currentIndex + 1) % _order.Count;
                if (_order[_currentIndex] != null && _order[_currentIndex].Stats != null)
                {
                    OnTurnChanged?.Invoke();
                    return;
                }
            }
            // Whole queue is dead — clear and notify so listeners don't render stale state.
            _order.Clear();
            _currentIndex = 0;
            OnTurnChanged?.Invoke();
        }

        /// <summary>Empty the queue. Used on state transitions (back to MainMenu, etc.).</summary>
        public void Clear()
        {
            _order.Clear();
            _currentIndex = 0;
            OnTurnChanged?.Invoke();
        }

        /// <summary>Remove entries whose CharacterStats has been destroyed. Useful after combat resolves.</summary>
        public void Compact()
        {
            int removed = _order.RemoveAll(e => e == null || e.Stats == null);
            if (removed == 0) return;
            if (_currentIndex >= _order.Count) _currentIndex = 0;
            OnTurnChanged?.Invoke();
        }
    }
}
