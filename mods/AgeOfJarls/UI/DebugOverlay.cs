using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// aoj_debug: a few lines over every settler nearby - what its ZDO says (the same on every machine) and, for the
    /// settlers this machine simulates, what its AI is doing (fight, home routine, job target, path, loot). Uses the
    /// game's NPC speech text, updated in place twice a second so it does not flicker. Only this player sees it.
    /// </summary>
    internal sealed class DebugOverlay : MonoBehaviour
    {
        private const float Range = 40f;
        private const float RefreshSeconds = 0.5f;
        private static readonly Vector3 Offset = new Vector3(0f, 2.4f, 0f);

        private static DebugOverlay s_instance;
        private float _timer;

        internal static bool Enabled => s_instance != null;

        /// <summary>On or off; returns the new state.</summary>
        internal static bool Toggle()
        {
            if (s_instance != null)
            {
                Destroy(s_instance.gameObject);
                s_instance = null;
                ClearTexts();
                return false;
            }
            var host = new GameObject("AoJ_DebugOverlay");
            DontDestroyOnLoad(host);
            s_instance = host.AddComponent<DebugOverlay>();
            return true;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }
            _timer = RefreshSeconds;
            Player player = Player.m_localPlayer;
            if (player == null || Chat.instance == null)
            {
                return;
            }
            foreach (Settler settler in Settler.Loaded)
            {
                if (settler != null && Vector3.Distance(settler.transform.position, player.transform.position) <= Range)
                {
                    Show(settler.gameObject, settler.DebugText());
                }
            }
        }

        private static void Show(GameObject settler, string text)
        {
            Chat.NpcText shown = Chat.instance.FindNpcText(settler);
            if (shown != null)
            {
                shown.m_text = text;
                shown.m_ttl = RefreshSeconds * 3f;
                shown.UpdateText();
                return;
            }
            Chat.instance.SetNpcText(settler, Offset, Range, RefreshSeconds * 3f, "", text, false);
        }

        private static void ClearTexts()
        {
            if (Chat.instance == null)
            {
                return;
            }
            foreach (Settler settler in Settler.Loaded)
            {
                if (settler != null)
                {
                    Chat.instance.ClearNpcText(settler.gameObject);
                }
            }
        }
    }
}
