using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DnD.UI
{
    /// <summary>
    /// Chat-based UI for D&D game interactions
    /// Uses TextMeshPro for text rendering and object pooling for performance
    /// </summary>
    public class ChatUI : MonoBehaviour
    {
        public static ChatUI Instance { get; private set; }

        [Header("UI References")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform contentPanel;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button sendButton;

        [Header("Message Prefabs")]
        [SerializeField] private GameObject playerMessagePrefab;
        [SerializeField] private GameObject dmMessagePrefab;
        [SerializeField] private GameObject systemMessagePrefab;

        [Header("Settings")]
        [SerializeField] private int maxMessages = 100;
        [SerializeField] private bool autoScroll = true;
        [SerializeField] private float typewriterSpeed = 0.03f;

        private Queue<GameObject> messagePool = new Queue<GameObject>();
        private List<GameObject> activeMessages = new List<GameObject>();
        private Coroutine typewriterCoroutine;

        public System.Action<string> OnPlayerInput;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

        private void Start()
        {
            if (sendButton != null)
                sendButton.onClick.AddListener(SendMessage);

            if (inputField != null)
            {
                inputField.onSubmit.AddListener((text) =>
                {
                    if (!string.IsNullOrWhiteSpace(text))
                        SendMessage();
                });
            }
        }

        public void AddPlayerMessage(string message)
        {
            AddMessage(message, MessageType.Player);
        }

        public void AddDMMessage(string message, bool useTypewriter = false)
        {
            if (useTypewriter)
            {
                if (typewriterCoroutine != null)
                    StopCoroutine(typewriterCoroutine);

                typewriterCoroutine = StartCoroutine(TypewriterEffect(message, MessageType.DM));
            }
            else
            {
                AddMessage(message, MessageType.DM);
            }
        }

        public void AddSystemMessage(string message)
        {
            AddMessage(message, MessageType.System);
        }

        private void AddMessage(string text, MessageType type)
        {
            GameObject messagePrefab = GetPrefabForType(type);
            if (messagePrefab == null)
            {
                Debug.LogError($"No prefab set for message type: {type}");
                return;
            }

            GameObject messageObj = Instantiate(messagePrefab, contentPanel);
            TMP_Text textComponent = messageObj.GetComponentInChildren<TMP_Text>();

            if (textComponent != null)
            {
                textComponent.text = text;
            }

            activeMessages.Add(messageObj);

            // Limit message history
            if (activeMessages.Count > maxMessages)
            {
                GameObject oldestMessage = activeMessages[0];
                activeMessages.RemoveAt(0);
                Destroy(oldestMessage);
            }

            if (autoScroll)
            {
                StartCoroutine(ScrollToBottom());
            }
        }

        private IEnumerator TypewriterEffect(string fullText, MessageType type)
        {
            GameObject messagePrefab = GetPrefabForType(type);
            GameObject messageObj = Instantiate(messagePrefab, contentPanel);
            TMP_Text textComponent = messageObj.GetComponentInChildren<TMP_Text>();

            if (textComponent != null)
            {
                textComponent.text = "";
                activeMessages.Add(messageObj);

                foreach (char c in fullText)
                {
                    textComponent.text += c;
                    yield return new WaitForSeconds(typewriterSpeed);

                    if (autoScroll)
                        Canvas.ForceUpdateCanvases();
                }

                if (autoScroll)
                {
                    yield return ScrollToBottom();
                }
            }

            typewriterCoroutine = null;
        }

        private IEnumerator ScrollToBottom()
        {
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private void SendMessage()
        {
            if (inputField == null || string.IsNullOrWhiteSpace(inputField.text))
                return;

            string message = inputField.text;
            inputField.text = "";
            inputField.ActivateInputField();

            // Display player message
            AddPlayerMessage(message);

            // Notify listeners (GameManager, CommandParser, etc.)
            OnPlayerInput?.Invoke(message);
        }

        private GameObject GetPrefabForType(MessageType type)
        {
            switch (type)
            {
                case MessageType.Player:
                    return playerMessagePrefab;
                case MessageType.DM:
                    return dmMessagePrefab;
                case MessageType.System:
                    return systemMessagePrefab;
                default:
                    return dmMessagePrefab;
            }
        }

        public void ClearChat()
        {
            foreach (var message in activeMessages)
            {
                if (message != null)
                    Destroy(message);
            }
            activeMessages.Clear();
        }

        public void AppendToDMMessage(string token)
        {
            // For streaming responses - append to the last DM message
            if (activeMessages.Count > 0)
            {
                GameObject lastMessage = activeMessages[activeMessages.Count - 1];
                TMP_Text textComponent = lastMessage.GetComponentInChildren<TMP_Text>();
                if (textComponent != null)
                {
                    textComponent.text += token;

                    if (autoScroll)
                    {
                        Canvas.ForceUpdateCanvases();
                        scrollRect.verticalNormalizedPosition = 0f;
                    }
                }
            }
        }

        public enum MessageType
        {
            Player,
            DM,
            System
        }
    }
}
