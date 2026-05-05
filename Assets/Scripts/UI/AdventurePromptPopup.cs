// Assets/Scripts/UI/AdventurePromptPopup.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DnD.UI
{
    public class AdventurePromptPopup : MonoBehaviour
    {
        public static AdventurePromptPopup Instance { get; private set; }

        public Action<string> OnSubmit;
        public Action         OnCancel;

        [SerializeField] private TMP_InputField promptInput;
        [SerializeField] private Button         beginButton;
        [SerializeField] private Button         cancelButton;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void OnEnable()
        {
            if (beginButton != null)
            {
                beginButton.onClick.RemoveAllListeners();
                beginButton.onClick.AddListener(SubmitPrompt);
            }
            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveAllListeners();
                cancelButton.onClick.AddListener(() => OnCancel?.Invoke());
            }
        }

        public void Open()
        {
            gameObject.SetActive(true);
            if (promptInput != null)
            {
                promptInput.text = "";
                promptInput.ActivateInputField();
            }
        }

        public void Close() => gameObject.SetActive(false);

        private void SubmitPrompt()
        {
            string text = promptInput != null ? promptInput.text?.Trim() : "";
            if (string.IsNullOrEmpty(text)) return;
            OnSubmit?.Invoke(text);
        }
    }
}
