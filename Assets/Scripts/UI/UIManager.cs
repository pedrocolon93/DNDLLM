using UnityEngine;
using UnityEngine.UI; // Assuming standard UI, or TMP
using TMPro; // Standard in newer Unity
using DNDLLM.Core;
using DNDLLM.Story;
using DNDLLM.Gameplay;

namespace DNDLLM.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("UI References")]
        public TMP_InputField inputField;
        public Transform chatContent; // The Content object of the ScrollView
        public GameObject messagePrefab; // Prefab with TextMeshProUGUI
        public Button sendButton;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else Destroy(gameObject);
        }

        private void Start()
        {
            // Auto-find if null
            if (inputField == null) inputField = FindObjectOfType<TMP_InputField>();
            
            if (chatContent == null) 
            {
                // Try to find the Content inside a ScrollView
                ScrollRect sr = FindObjectOfType<ScrollRect>();
                if (sr != null) chatContent = sr.content;
            }

            if (sendButton == null) sendButton = FindObjectOfType<Button>();

            if (sendButton) sendButton.onClick.AddListener(OnSubmit);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += OnGameStateChanged;
                // Manually trigger for current state if we missed the event
                OnGameStateChanged(GameManager.Instance.CurrentState); 
            }
            
            SetupLayout();
        }

        private void SetupLayout()
        {
            if (chatContent == null) return;

            // Ensure VerticalLayoutGroup
            VerticalLayoutGroup layoutGroup = chatContent.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup == null) layoutGroup = chatContent.gameObject.AddComponent<VerticalLayoutGroup>();

            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandHeight = false; // Let children determine their height
            layoutGroup.childForceExpandWidth = true;   // Stretch to width
            layoutGroup.spacing = 10f;
            layoutGroup.padding = new RectOffset(10, 10, 10, 10);

            // Ensure ContentSizeFitter
            ContentSizeFitter fitter = chatContent.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = chatContent.gameObject.AddComponent<ContentSizeFitter>();

            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        private void OnGameStateChanged(GameState newState)
        {
            if (newState == GameState.Setup || newState == GameState.StoryGeneration)
            {
                AddToLog("--- SYSTEM: GAME READY ---");
                AddToLog("Enter your story idea to begin...");
            }
        }

        public void OnSubmit()
        {
            if (string.IsNullOrWhiteSpace(inputField.text)) return;

            string userText = inputField.text;
            AddToLog($"Player: {userText}");
            inputField.text = "";

            HandleInput(userText);
        }

        private void HandleInput(string text)
        {
            if (GameManager.Instance == null)
            {
                AddToLog("Error: GameManager not found! Make sure it is in the scene.");
                Debug.LogError("GameManager.Instance is null!");
                return;
            }

            if (GameManager.Instance.CurrentState == GameState.Setup || GameManager.Instance.CurrentState == GameState.StoryGeneration)
            {
                // Input acts as story prompt
                 GameManager.Instance.StartNewGame(text);
                 
                 if (StoryEngine.Instance != null)
                 {
                    StoryEngine.Instance.GenerateStoryIntroduction(text);
                 }
                 else
                 {
                    AddToLog("Error: StoryEngine not found!");
                    Debug.LogError("StoryEngine.Instance is null!");
                 }
            }
            else if (GameManager.Instance.CurrentState == GameState.GameplayLoop)
            {
                if (ActionHandler.Instance != null)
                {
                    // Fire and forget task
                    _ = ActionHandler.Instance.ProcessPlayerAction(text);
                }
                else
                {
                    AddToLog("Error: ActionHandler not found!");
                     Debug.LogError("ActionHandler.Instance is null!");
                }
            }
            else
            {
                 Debug.LogWarning($"Input ignored in state {GameManager.Instance.CurrentState}");
            }
        }

        public void AddToLog(string message)
        {
            if (chatContent == null) return;

            GameObject newMsgObj;
            if (messagePrefab != null)
            {
                newMsgObj = Instantiate(messagePrefab, chatContent);
            }
            else
            {
                // Create default text object if no prefab
                newMsgObj = new GameObject("Message");
                // TextMeshProUGUI needs a RectTransform
                newMsgObj.AddComponent<RectTransform>();
                newMsgObj.transform.SetParent(chatContent, false);
                
                var textComp = newMsgObj.AddComponent<TextMeshProUGUI>();
                textComp.fontSize = 24;
                textComp.color = Color.white;
                textComp.enableWordWrapping = true;
                textComp.alignment = TextAlignmentOptions.TopLeft;
            }

            // Set Text
            TextMeshProUGUI tmp = newMsgObj.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = message;

            Debug.Log($"[UI] {message}");

            // Auto-scroll with delay to allow layout rebuild
            StartCoroutine(ScrollToBottom());
        }

        private System.Collections.IEnumerator ScrollToBottom()
        {
            // Wait for end of frame to ensure TextMeshPro and ContentSizeFitters have updated
            yield return new WaitForEndOfFrame();
            
            // Force update just in case
            Canvas.ForceUpdateCanvases();
            
            if (chatContent != null)
            {
                ScrollRect scrollRect = chatContent.GetComponentInParent<ScrollRect>();
                if (scrollRect != null)
                {
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }
    }
}
