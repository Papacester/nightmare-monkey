using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;


namespace Archipelago.UI
{
    public static class Schedule1PanelBuilder
    {
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

            // Create input field container
            GameObject inputGO = new GameObject("HostInput");
            inputGO.transform.SetParent(panel.transform, false);

            // Add background image
            Image inputBG = inputGO.AddComponent<Image>();
            inputBG.color = new Color(0.2f, 0.2f, 0.2f, 1f); // dark gray

            // Add InputField component
            InputField inputField = inputGO.AddComponent<InputField>();

            // Add placeholder text
            GameObject placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(inputGO.transform, false);
            Text placeholder = placeholderGO.AddComponent<Text>();
            placeholder.text = "address:port";
            placeholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            placeholder.fontSize = 18;
            placeholder.color = new Color(1f, 1f, 1f, 0.5f);
            placeholder.alignment = TextAnchor.MiddleLeft;

            // Add text component
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(inputGO.transform, false);
            Text inputText = textGO.AddComponent<Text>();
            inputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            inputText.fontSize = 18;
            inputText.color = Color.white;
            inputText.alignment = TextAnchor.MiddleLeft;

            // Wire up InputField
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;

            // Layout
            RectTransform inputRect = inputGO.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.5f, 0f);
            inputRect.anchorMax = new Vector2(0.5f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.anchoredPosition = new Vector2(0, 80); // just above the button
            inputRect.sizeDelta = new Vector2(300, 40);

            // Layout for placeholder and text
            RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;

            RectTransform textRect = inputText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

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

            button.onClick.AddListener(() => {
                string host = inputField.text;
                string playerName = nameField.text;
                string password = passField.text;

                // Simulate connection logic
                bool success = !string.IsNullOrEmpty(host) && host.Contains(":");

                if (Schedule1PanelManager.StatusText != null)
                {
                    if (success)
                    {
                        Schedule1PanelManager.StatusText.text = "Connected successfully!";
                        Schedule1PanelManager.StatusText.color = Color.green;
                    }
                    else
                    {
                        Schedule1PanelManager.StatusText.text = "Failed to connect.";
                        Schedule1PanelManager.StatusText.color = Color.red;
                    }
                }
            });


            MelonLogger.Msg("Schedule1Panel Created!");
            return panel;
        }
    }

}
