using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Narcopelago
{
    public static class NarcopelagoUI
    {
        // Event that fires when Connect button is clicked (host, port, slotName, password)
        public static event Action<string, int, string, string> OnConnectClicked;

        // Call this to update the status text from connection result
        public static void SetConnectionStatus(bool success, string message)
        {
            if (Schedule1PanelManager.StatusText != null)
            {
                Schedule1PanelManager.StatusText.text = message;
                Schedule1PanelManager.StatusText.color = success ? Color.green : Color.red;
            }
        }

        public static GameObject CreatePanel(Transform parent)
        {
            if (GameObject.Find("Schedule1Panel") != null)
                return GameObject.Find("Schedule1Panel");

            GameObject panel = new GameObject("Schedule1Panel");
            RectTransform rect = panel.AddComponent<RectTransform>();
            CanvasRenderer renderer = panel.AddComponent<CanvasRenderer>();
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f); // semi-transparent dark background

            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(400, 300);
            rect.anchoredPosition = new Vector2(-40f, 40f);

            GameObject canvas = GameObject.Find("MainMenu"); // adjust name if needed
            MelonLogger.Msg("Canvas found: " + (canvas != null));
            panel.transform.SetParent(canvas.transform, false);

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panel.transform, false);
            Text title = titleGO.AddComponent<Text>();
            title.text = "Connect to Archipelago";
            title.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            title.fontSize = 24;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = Color.white;

            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0, -30);
            titleRect.sizeDelta = new Vector2(300, 40);

            GameObject nameGO = new GameObject("NameInput");
            nameGO.transform.SetParent(panel.transform, false);

            Image nameBG = nameGO.AddComponent<Image>();
            nameBG.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            InputField nameField = nameGO.AddComponent<InputField>();

            GameObject namePlaceholderGO = new GameObject("Placeholder");
            namePlaceholderGO.transform.SetParent(nameGO.transform, false);
            Text namePlaceholder = namePlaceholderGO.AddComponent<Text>();
            namePlaceholder.text = "Slot Name";
            namePlaceholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            namePlaceholder.fontSize = 18;
            namePlaceholder.color = new Color(1f, 1f, 1f, 0.5f);
            namePlaceholder.alignment = TextAnchor.MiddleLeft;

            GameObject nameTextGO = new GameObject("Text");
            nameTextGO.transform.SetParent(nameGO.transform, false);
            Text nameText = nameTextGO.AddComponent<Text>();
            nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            nameText.fontSize = 18;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.MiddleLeft;

            nameField.textComponent = nameText;
            nameField.placeholder = namePlaceholder;

            RectTransform nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0.5f, 0f);
            nameRect.anchorMax = new Vector2(0.5f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.anchoredPosition = new Vector2(0, 180);
            nameRect.sizeDelta = new Vector2(300, 40);

            RectTransform namePlaceholderRect = namePlaceholder.GetComponent<RectTransform>();
            namePlaceholderRect.anchorMin = Vector2.zero;
            namePlaceholderRect.anchorMax = Vector2.one;
            namePlaceholderRect.offsetMin = Vector2.zero;
            namePlaceholderRect.offsetMax = Vector2.zero;

            RectTransform nameTextRect = nameText.GetComponent<RectTransform>();
            nameTextRect.anchorMin = Vector2.zero;
            nameTextRect.anchorMax = Vector2.one;
            nameTextRect.offsetMin = Vector2.zero;
            nameTextRect.offsetMax = Vector2.zero;

            GameObject passGO = new GameObject("PasswordInput");
            passGO.transform.SetParent(panel.transform, false);

            Image passBG = passGO.AddComponent<Image>();
            passBG.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            InputField passField = passGO.AddComponent<InputField>();
            passField.contentType = InputField.ContentType.Password;

            GameObject passPlaceholderGO = new GameObject("Placeholder");
            passPlaceholderGO.transform.SetParent(passGO.transform, false);
            Text passPlaceholder = passPlaceholderGO.AddComponent<Text>();
            passPlaceholder.text = "Password";
            passPlaceholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            passPlaceholder.fontSize = 18;
            passPlaceholder.color = new Color(1f, 1f, 1f, 0.5f);
            passPlaceholder.alignment = TextAnchor.MiddleLeft;

            GameObject passTextGO = new GameObject("Text");
            passTextGO.transform.SetParent(passGO.transform, false);
            Text passText = passTextGO.AddComponent<Text>();
            passText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            passText.fontSize = 18;
            passText.color = Color.white;
            passText.alignment = TextAnchor.MiddleLeft;

            passField.textComponent = passText;
            passField.placeholder = passPlaceholder;

            RectTransform passRect = passGO.GetComponent<RectTransform>();
            passRect.anchorMin = new Vector2(0.5f, 0f);
            passRect.anchorMax = new Vector2(0.5f, 0f);
            passRect.pivot = new Vector2(0.5f, 0f);
            passRect.anchoredPosition = new Vector2(0, 130);
            passRect.sizeDelta = new Vector2(300, 40);

            RectTransform passPlaceholderRect = passPlaceholder.GetComponent<RectTransform>();
            passPlaceholderRect.anchorMin = Vector2.zero;
            passPlaceholderRect.anchorMax = Vector2.one;
            passPlaceholderRect.offsetMin = Vector2.zero;
            passPlaceholderRect.offsetMax = Vector2.zero;

            RectTransform passTextRect = passText.GetComponent<RectTransform>();
            passTextRect.anchorMin = Vector2.zero;
            passTextRect.anchorMax = Vector2.one;
            passTextRect.offsetMin = Vector2.zero;
            passTextRect.offsetMax = Vector2.zero;

            // Create Host input field
            GameObject hostGO = new GameObject("HostInput");
            hostGO.transform.SetParent(panel.transform, false);

            Image hostBG = hostGO.AddComponent<Image>();
            hostBG.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            InputField hostField = hostGO.AddComponent<InputField>();

            GameObject hostPlaceholderGO = new GameObject("Placeholder");
            hostPlaceholderGO.transform.SetParent(hostGO.transform, false);
            Text hostPlaceholder = hostPlaceholderGO.AddComponent<Text>();
            hostPlaceholder.text = "Host (e.g. localhost)";
            hostPlaceholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hostPlaceholder.fontSize = 18;
            hostPlaceholder.color = new Color(1f, 1f, 1f, 0.5f);
            hostPlaceholder.alignment = TextAnchor.MiddleLeft;

            GameObject hostTextGO = new GameObject("Text");
            hostTextGO.transform.SetParent(hostGO.transform, false);
            Text hostText = hostTextGO.AddComponent<Text>();
            hostText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hostText.fontSize = 18;
            hostText.color = Color.white;
            hostText.alignment = TextAnchor.MiddleLeft;

            hostField.textComponent = hostText;
            hostField.placeholder = hostPlaceholder;

            RectTransform hostRect = hostGO.GetComponent<RectTransform>();
            hostRect.anchorMin = new Vector2(0.5f, 0f);
            hostRect.anchorMax = new Vector2(0.5f, 0f);
            hostRect.pivot = new Vector2(0.5f, 0f);
            hostRect.anchoredPosition = new Vector2(-55, 80);
            hostRect.sizeDelta = new Vector2(190, 40);

            RectTransform hostPlaceholderRect = hostPlaceholder.GetComponent<RectTransform>();
            hostPlaceholderRect.anchorMin = Vector2.zero;
            hostPlaceholderRect.anchorMax = Vector2.one;
            hostPlaceholderRect.offsetMin = Vector2.zero;
            hostPlaceholderRect.offsetMax = Vector2.zero;

            RectTransform hostTextRect = hostText.GetComponent<RectTransform>();
            hostTextRect.anchorMin = Vector2.zero;
            hostTextRect.anchorMax = Vector2.one;
            hostTextRect.offsetMin = Vector2.zero;
            hostTextRect.offsetMax = Vector2.zero;

            // Create Port input field
            GameObject portGO = new GameObject("PortInput");
            portGO.transform.SetParent(panel.transform, false);

            Image portBG = portGO.AddComponent<Image>();
            portBG.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            InputField portField = portGO.AddComponent<InputField>();
            portField.contentType = InputField.ContentType.IntegerNumber;

            GameObject portPlaceholderGO = new GameObject("Placeholder");
            portPlaceholderGO.transform.SetParent(portGO.transform, false);
            Text portPlaceholder = portPlaceholderGO.AddComponent<Text>();
            portPlaceholder.text = "Port";
            portPlaceholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            portPlaceholder.fontSize = 18;
            portPlaceholder.color = new Color(1f, 1f, 1f, 0.5f);
            portPlaceholder.alignment = TextAnchor.MiddleLeft;

            GameObject portTextGO = new GameObject("Text");
            portTextGO.transform.SetParent(portGO.transform, false);
            Text portText = portTextGO.AddComponent<Text>();
            portText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            portText.fontSize = 18;
            portText.color = Color.white;
            portText.alignment = TextAnchor.MiddleLeft;

            portField.textComponent = portText;
            portField.placeholder = portPlaceholder;

            RectTransform portRect = portGO.GetComponent<RectTransform>();
            portRect.anchorMin = new Vector2(0.5f, 0f);
            portRect.anchorMax = new Vector2(0.5f, 0f);
            portRect.pivot = new Vector2(0.5f, 0f);
            portRect.anchoredPosition = new Vector2(100, 80);
            portRect.sizeDelta = new Vector2(100, 40);

            RectTransform portPlaceholderRect = portPlaceholder.GetComponent<RectTransform>();
            portPlaceholderRect.anchorMin = Vector2.zero;
            portPlaceholderRect.anchorMax = Vector2.one;
            portPlaceholderRect.offsetMin = Vector2.zero;
            portPlaceholderRect.offsetMax = Vector2.zero;

            RectTransform portTextRect = portText.GetComponent<RectTransform>();
            portTextRect.anchorMin = Vector2.zero;
            portTextRect.anchorMax = Vector2.one;
            portTextRect.offsetMin = Vector2.zero;
            portTextRect.offsetMax = Vector2.zero;

            //Confirm Button
            GameObject buttonGO = new GameObject("ConfirmButton");
            buttonGO.transform.SetParent(panel.transform, false);
            Button button = buttonGO.AddComponent<Button>();
            Image buttonImage = buttonGO.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.6f, 0.2f, 1f); // green

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0, 30);
            buttonRect.sizeDelta = new Vector2(160, 40);

            // Add button label
            GameObject labelGO = new GameObject("ButtonLabel");
            labelGO.transform.SetParent(buttonGO.transform, false);
            Text label = labelGO.AddComponent<Text>();
            label.text = "Connect";
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;

            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            GameObject statusGO = new GameObject("StatusText");
            statusGO.transform.SetParent(panel.transform, false);

            Text statusText = statusGO.AddComponent<Text>();
            statusText.text = ""; // start empty
            statusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statusText.fontSize = 16;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = Color.white;

            RectTransform statusRect = statusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.5f, 0f);
            statusRect.anchorMax = new Vector2(0.5f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0, 5); // just above the button
            statusRect.sizeDelta = new Vector2(300, 30);

            Schedule1PanelManager.StatusText = statusText;
            Schedule1PanelManager.HostField = hostField;
            Schedule1PanelManager.PortField = portField;
            Schedule1PanelManager.SlotNameField = nameField;
            Schedule1PanelManager.PasswordField = passField;

            button.onClick.AddListener((UnityAction)OnConnectButtonClicked);

            void OnConnectButtonClicked()
            {
                string host = hostField.text;
                string portStr = portField.text;
                string playerName = nameField.text;
                string password = passField.text;

                int port = 38281; // default port
                int.TryParse(portStr, out port);

                // Show connecting status
                if (Schedule1PanelManager.StatusText != null)
                {
                    Schedule1PanelManager.StatusText.text = "Connecting...";
                    Schedule1PanelManager.StatusText.color = Color.yellow;
                }
        
                // Invoke the event so subscribers can handle connection
                OnConnectClicked?.Invoke(host, port, playerName, password);
            }


            MelonLogger.Msg("Schedule1Panel Created!");
            return panel;
        }
    }
}
