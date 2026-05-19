// Assets/Scripts/UI/AdventurePromptPopup.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DnD.AI;

namespace DnD.UI
{
    public class AdventurePromptPopup : MonoBehaviour
    {
        public static AdventurePromptPopup Instance { get; private set; }

        /// <summary>Legacy single-string callback (no size). Kept so older wiring still compiles.</summary>
        public Action<string>                OnSubmit;
        /// <summary>Preferred — fires alongside OnSubmit with the size selection.</summary>
        public Action<string, CampaignSize>  OnSubmitWithSize;
        public Action                        OnCancel;

        [SerializeField] private TMP_InputField promptInput;
        [SerializeField] private Button         beginButton;
        [SerializeField] private Button         cancelButton;

        [Header("Campaign size buttons (Small/Medium/Large)")]
        [SerializeField] private Button smallButton;
        [SerializeField] private Button mediumButton;
        [SerializeField] private Button largeButton;
        [SerializeField] private TMP_Text sizeLabel;   // optional readout — "Medium (7×7)"

        private static readonly Color32 SizeSelected = new Color32(0xC8, 0xA0, 0x50, 0xFF);
        private static readonly Color32 SizeNormal   = new Color32(0x2A, 0x1F, 0x0E, 0xFF);

        private CampaignSize _selectedSize = CampaignSize.Medium;
        public  CampaignSize SelectedSize => _selectedSize;

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
            WireSizeButton(smallButton,  CampaignSize.Small);
            WireSizeButton(mediumButton, CampaignSize.Medium);
            WireSizeButton(largeButton,  CampaignSize.Large);
            RefreshSizeButtons();
        }

        private void WireSizeButton(Button btn, CampaignSize size)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => SelectSize(size));
        }

        public void SelectSize(CampaignSize size)
        {
            _selectedSize = size;
            RefreshSizeButtons();
        }

        private void RefreshSizeButtons()
        {
            TintSizeButton(smallButton,  _selectedSize == CampaignSize.Small);
            TintSizeButton(mediumButton, _selectedSize == CampaignSize.Medium);
            TintSizeButton(largeButton,  _selectedSize == CampaignSize.Large);
            if (sizeLabel != null)
            {
                int dim = CampaignSizeInfo.MapDim(_selectedSize);
                sizeLabel.text = $"{CampaignSizeInfo.Label(_selectedSize)} ({dim}×{dim})";
            }
        }

        private static void TintSizeButton(Button btn, bool selected)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = selected ? (Color)SizeSelected : (Color)SizeNormal;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            if (promptInput != null)
            {
                promptInput.text = "";
                promptInput.ActivateInputField();
            }
            RefreshSizeButtons();
        }

        public void Close() => gameObject.SetActive(false);

        private void SubmitPrompt()
        {
            string text = promptInput != null ? promptInput.text?.Trim() : "";
            if (string.IsNullOrEmpty(text)) return;
            OnSubmitWithSize?.Invoke(text, _selectedSize);
            OnSubmit?.Invoke(text);
        }
    }
}
