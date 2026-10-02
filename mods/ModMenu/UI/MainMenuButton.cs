using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModMenu.UI
{
    /// <summary>Adds a "Mods" button under "Settings" in the main menu's button list.</summary>
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
    internal static class MainMenuButton
    {
        private const string ButtonName = "ModMenuButton";

        private static void Postfix(FejdStartup __instance)
        {
            try
            {
                Add(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Could not add the main menu button: {e}");
            }
        }

        private static void Add(FejdStartup startup)
        {
            GameObject list = startup.m_menuList;
            // The buttons may sit in sub-groups of the list, so look at every level, not only direct children.
            if (list == null || list.GetComponentsInChildren<Transform>(true).Any(t => t.name == ButtonName))
            {
                return;
            }
            Button[] buttons = list.GetComponentsInChildren<Button>(true);
            Button template = buttons.FirstOrDefault(b => Calls(b, nameof(FejdStartup.OnButtonSettings))) ?? buttons.LastOrDefault();
            if (template == null)
            {
                Plugin.Log.LogWarning("Main menu has no buttons to copy; the Mods button was not added.");
                return;
            }

            GameObject copy = Object.Instantiate(template.gameObject, template.transform.parent);
            copy.name = ButtonName;
            // Localize components would turn the label back into "Settings" on a language change.
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour.GetType().Name == "Localize")
                {
                    Object.Destroy(behaviour);
                }
            }
            // A copied key/gamepad shortcut would press both buttons at once.
            foreach (UIGamePad pad in copy.GetComponentsInChildren<UIGamePad>(true))
            {
                if (pad.m_hint != null)
                {
                    Object.Destroy(pad.m_hint);
                }
                Object.Destroy(pad);
            }
            SetLabel(copy, T.Get("menu_button"));

            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => OpenWindow(startup));

            if (copy.transform.parent.GetComponent<LayoutGroup>() != null)
            {
                copy.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            }
            else
            {
                // Hand-placed list: put the new button one step below the last one.
                Button[] ordered = buttons.OrderByDescending(b => ((RectTransform)b.transform).anchoredPosition.y).ToArray();
                if (ordered.Length >= 2)
                {
                    var last = (RectTransform)ordered[ordered.Length - 1].transform;
                    var before = (RectTransform)ordered[ordered.Length - 2].transform;
                    ((RectTransform)copy.transform).anchoredPosition = last.anchoredPosition + (last.anchoredPosition - before.anchoredPosition);
                }
            }

            // Keyboard / gamepad navigation walks this array.
            startup.m_menuButtons = list.GetComponentsInChildren<Button>();
            Plugin.Log.LogInfo($"Main menu button added after \"{template.name}\"");
        }

        internal static void OpenWindow(FejdStartup startup)
        {
            startup.m_menuList.SetActive(false);
            ModMenuWindow.Open(() =>
            {
                if (startup != null)
                {
                    startup.m_menuList.SetActive(true);
                }
            });
        }

        private static bool Calls(Button button, string method)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == method)
                {
                    return true;
                }
            }
            return false;
        }

        private static void SetLabel(GameObject button, string text)
        {
            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = text;
                return;
            }
            Text legacy = button.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                legacy.text = text;
            }
        }
    }
}
