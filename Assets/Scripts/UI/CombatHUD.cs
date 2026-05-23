using UnityEngine;
using TMPro;
using DnD.Managers;
using DnD.Character;
using DnD.Core;

namespace DnD.UI
{
    public class CombatHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text turnLabel;
        [SerializeField] private TMP_Text hpLabel;
        [SerializeField] private GameObject root;

        private TurnQueue _subscribedTurns;

        private void OnEnable()
        {
            TrySubscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (_subscribedTurns == null) TrySubscribe();
            Refresh();
        }

        private void TrySubscribe()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var turns = gm.Turns;
            if (turns == null) return;
            if (_subscribedTurns == turns) return;

            Unsubscribe();
            turns.OnActorChanged += HandleActorChanged;
            _subscribedTurns = turns;
        }

        private void Unsubscribe()
        {
            if (_subscribedTurns != null)
            {
                _subscribedTurns.OnActorChanged -= HandleActorChanged;
                _subscribedTurns = null;
            }
        }

        private void HandleActorChanged(CharacterStats _) => Refresh();

        private void Refresh()
        {
            var gm = GameManager.Instance;
            var turns = gm != null ? gm.Turns : null;
            bool show = turns != null && turns.IsCombat;

            if (root != null && root.activeSelf != show) root.SetActive(show);
            if (!show) return;

            var actor = turns.CurrentActor;
            if (actor == null)
            {
                if (turnLabel != null) turnLabel.text = "Turn: —";
                if (hpLabel != null)   hpLabel.text   = "HP: — / —";
                return;
            }

            if (turnLabel != null) turnLabel.text = $"Turn: {actor.characterName}";
            if (hpLabel != null)   hpLabel.text   = $"HP: {actor.currentHitPoints} / {actor.maxHitPoints}";
        }
    }
}
