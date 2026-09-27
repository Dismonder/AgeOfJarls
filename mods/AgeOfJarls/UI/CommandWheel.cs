using System;
using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;
using Valheim.UI;
using Object = UnityEngine.Object;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Quick commands in the game's own radial menu, the one emotes are picked from. The wheel key (H, the way T opens
    /// the emotes) gives the orders of the settler the player looks at - from afar too - or, looking elsewhere, the
    /// squad's orders with the creature in sight as the target. The game's wheel (G) gets a "Commands" group for the
    /// squad and gives a settler's orders when the crosshair is on it. Orders are the same RPCs the order window
    /// sends, so they reach whichever player simulates the settler; the player makes the matching gesture.
    /// </summary>
    internal sealed class CommandWheel : IRadialConfig
    {
        /// <summary>How far off the crosshair (degrees) the wheel key still picks a settler, and "Attack!" a creature.</summary>
        private const float LooseAimDegrees = 6f;
        private const float EnemyAimDegrees = 20f;
        /// <summary>How far around a settler "Attack!" from its own wheel looks for an enemy.</summary>
        private const float SettlerAttackRange = 25f;
        /// <summary>A settler's window opened from afar stays open until the player is this much farther away.</summary>
        private const float WindowSlack = 10f;
        /// <summary>The wheel's title shows how far the settler is from here on.</summary>
        private const int ShowDistanceFrom = 6;
        /// <summary>Where the group goes in the main wheel: right after the emotes.</summary>
        private const int MainWheelSlot = 5;

        private static ButtonConfig s_key;
        private static int s_sightMask;

        private enum Order
        {
            Follow,
            Wait,
            Attack,
            FallBack,
            GoHome,
        }

        // One settler, or the squad (optionally with the creature looked at when the wheel opened as the target).
        // _one stays true when the settler unloads while the wheel is open, so its orders never go to the squad.
        private readonly bool _one;
        private readonly Settler _single;
        private readonly Character _target;

        private CommandWheel(Settler single, Character target)
        {
            _one = single != null;
            _single = single;
            _target = target;
        }

        public string LocalizedName
        {
            get
            {
                if (!_one)
                {
                    return Localize("$aoj_cmd_group");
                }
                if (_single == null)
                {
                    return "";
                }
                int distance = DistanceTo(_single.transform.position);
                return distance >= ShowDistanceFrom ? $"{_single.DisplayName} ({distance} m)" : _single.DisplayName;
            }
        }

        public Sprite Sprite => GestureSprite(Emotes.Point);

        /// <summary>The wheel key as the player sees it, for hints.</summary>
        internal static string KeyName => AoJConfig.CommandWheelKey.Value.ToString();

        /// <summary>The hover line about the wheel key (a localization text), or nothing when the key is unbound.</summary>
        internal static string HoverHint => AoJConfig.CommandWheelKey.Value == KeyCode.None
            ? ""
            : $"[<color=yellow><b>{KeyName}</b></color>] $aoj_cmd_wheel_hover\n";

        /// <summary>Registers the wheel key with the game's input; it is rebound in the config (Commands/WheelKey).</summary>
        internal static void RegisterKey()
        {
            s_key = new ButtonConfig { Name = "AoJ_CommandWheel", Config = AoJConfig.CommandWheelKey };
            InputManager.Instance.AddButton(PluginInfo.Guid, s_key);
        }

        public void InitRadialConfig(RadialBase radial)
        {
            var elements = new List<RadialMenuElement>();
            if (_one)
            {
                elements.Add(Element(Emotes.ComeHere, "$aoj_cmd_call", "$aoj_cmd_call_desc", () => Give(Order.Follow, Emotes.ComeHere, "$aoj_cmd_call")));
                elements.Add(Element(Emotes.Point, "$aoj_cmd_stay", "$aoj_cmd_stay_desc", () => Give(Order.Wait, Emotes.Point, "$aoj_cmd_stay")));
                elements.Add(Element(Emotes.Challenge, "$aoj_cmd_attack", "$aoj_cmd_attack_one", () => Give(Order.Attack, Emotes.Challenge, "$aoj_cmd_attack")));
                elements.Add(Element(Emotes.NoNoNo, "$aoj_cmd_fallback", "$aoj_cmd_fallback_one", () => Give(Order.FallBack, Emotes.NoNoNo, "$aoj_cmd_fallback")));
                if (_single != null && _single.HasHome)
                {
                    elements.Add(Element(Emotes.Wave, "$aoj_cmd_home", "$aoj_cmd_home_one", () => Give(Order.GoHome, Emotes.Wave, "$aoj_cmd_home")));
                }
                elements.Add(Element(Emotes.ThumbsUp, "$aoj_cmd_details", "$aoj_cmd_details_desc", OpenDetails));
            }
            else
            {
                string attack = _target != null
                    ? Localize("$aoj_cmd_attack_target", _target.GetHoverName(), DistanceTo(_target.transform.position).ToString())
                    : "$aoj_cmd_attack_desc";
                elements.Add(Element(Emotes.ComeHere, "$aoj_cmd_follow", "$aoj_cmd_follow_desc", () => Give(Order.Follow, Emotes.ComeHere, "$aoj_cmd_follow")));
                elements.Add(Element(Emotes.Point, "$aoj_cmd_wait", "$aoj_cmd_wait_desc", () => Give(Order.Wait, Emotes.Point, "$aoj_cmd_wait")));
                elements.Add(Element(Emotes.Challenge, "$aoj_cmd_attack", attack, () => Give(Order.Attack, Emotes.Challenge, "$aoj_cmd_attack")));
                elements.Add(Element(Emotes.NoNoNo, "$aoj_cmd_fallback", "$aoj_cmd_fallback_desc", () => Give(Order.FallBack, Emotes.NoNoNo, "$aoj_cmd_fallback")));
                elements.Add(Element(Emotes.Wave, "$aoj_cmd_home", "$aoj_cmd_home_desc", () => Give(Order.GoHome, Emotes.Wave, "$aoj_cmd_home")));
            }
            radial.ConstructRadial(elements);
        }

        // Elements are the game's emote element with our text, icon and action, like vanilla builds its own.
        private static RadialMenuElement Element(Emotes icon, string name, string description, Func<bool> action)
        {
            EmoteElement element = Object.Instantiate(RadialData.SO.EmoteElement);
            element.Name = Localize(name);
            element.Description = Localize(description);
            element.Interact = action;
            element.CloseOnInteract = () => true;
            Sprite sprite = GestureSprite(icon);
            element.m_icon.gameObject.SetActive(sprite != null);
            element.m_icon.sprite = sprite;
            return element;
        }

        private bool Give(Order order, Emotes gesture, string label)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }
            if (_one && (_single == null || _single.IsCaptive))
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_cmd_lost"));
                return true;
            }

            List<Settler> settlers = Targets(order, player, out int refused);
            if (settlers.Count == 0)
            {
                string reason = _one ? Localize("$aoj_msg_cmd_not_yours", _single.DisplayName)
                    : refused > 0 ? Localize("$aoj_msg_cmd_refused")
                    : Localize(order == Order.Follow ? "$aoj_msg_cmd_nobody_near" : "$aoj_msg_cmd_nobody");
                player.Message(MessageHud.MessageType.Center, reason);
                return true;
            }

            Character enemy = null;
            if (order == Order.Attack)
            {
                enemy = _one ? NearestEnemy(_single.transform.position) : IsValidEnemy(_target) ? _target : LookedAtEnemy(player);
                if (enemy == null)
                {
                    player.Message(MessageHud.MessageType.Center, _one
                        ? Localize("$aoj_msg_cmd_no_target_near", _single.DisplayName)
                        : Localize("$aoj_msg_cmd_no_target"));
                    return true;
                }
            }

            foreach (Settler settler in settlers)
            {
                switch (order)
                {
                    case Order.Follow:
                        settler.SendFollow(player);
                        break;
                    case Order.Wait:
                        settler.SendWait();
                        break;
                    case Order.Attack:
                        settler.SendAttack(enemy);
                        break;
                    case Order.FallBack:
                        settler.SendFallBack(player);
                        break;
                    case Order.GoHome:
                        settler.SendGoHome();
                        break;
                }
            }

            if (AoJConfig.CommandGestures.Value)
            {
                Emote.DoEmote(gesture);
            }
            string orderName = Localize(label);
            player.Message(MessageHud.MessageType.Center, settlers.Count == 1
                ? Localize("$aoj_msg_cmd_one", settlers[0].DisplayName, orderName)
                : Localize("$aoj_msg_cmd_many", orderName, settlers.Count.ToString()));
            Log.Debug("UI", $"Order {order} to {settlers.Count} settler(s){(enemy != null ? " against " + enemy.m_name : "")}");
            return true;
        }

        private bool OpenDetails()
        {
            if (_single == null)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_cmd_lost"));
                return true;
            }
            SettlerWindow.Open(_single, DistanceTo(_single.transform.position) + WindowSlack);
            return true;
        }

        // One settler: the one looked at. Squad: the settlers following this player; "follow me" also calls the ones
        // nearby that follow nobody else, and "go home" only concerns settlers that have a home. Settlers that would
        // refuse the order are counted, not sent, so the message tells the truth.
        private List<Settler> Targets(Order order, Player player, out int refused)
        {
            var targets = new List<Settler>();
            refused = 0;
            if (_one)
            {
                if (Accepts(_single, order, player))
                {
                    targets.Add(_single);
                }
                else
                {
                    refused = 1;
                }
                return targets;
            }

            long me = player.GetPlayerID();
            float range = AoJConfig.CommandRange.Value;
            foreach (Settler settler in Settler.Loaded)
            {
                if (settler == null || settler.Identity == null || settler.IsCaptive)
                {
                    continue;
                }
                long leader = settler.FollowedPlayerId;
                bool mine = leader == me;
                bool free = order == Order.Follow && leader == 0L && Vector3.Distance(settler.transform.position, player.transform.position) <= range;
                if ((!mine && !free) || (order == Order.GoHome && !settler.HasHome))
                {
                    continue;
                }
                if (Accepts(settler, order, player))
                {
                    targets.Add(settler);
                }
                else
                {
                    refused++;
                }
            }
            return targets;
        }

        // The owner's checks (Settler.RPC_*): anybody may call a settler, stop it or pull it back; a fight needs its
        // leader or an officer (Huskarl and up) of its settlement, going home its leader or a member.
        private static bool Accepts(Settler settler, Order order, Player player)
        {
            bool leader = settler.FollowedPlayerId == player.GetPlayerID();
            switch (order)
            {
                case Order.Attack:
                    return leader || settler.MayBeOrderedBy(player, SettlementRight.Military);
                case Order.GoHome:
                    return settler.HasHome && (leader || settler.MayBeOrderedBy(player, SettlementRight.Live));
                default:
                    return true;
            }
        }

        // ---------------------------------------------------------------- what the player looks at

        /// <summary>
        /// The settler the player looks at: the one under the crosshair (the game's own aim, which walls block) within
        /// Commands/LookRange, or - loose - the one closest to the crosshair inside a narrow cone, in sight.
        /// Captives are left out: they take no orders until freed.
        /// </summary>
        internal static Settler LookedAtSettler(Player player, bool loose)
        {
            float range = AoJConfig.CommandLookRange.Value;
            GameObject hover = player.GetHoverObject();
            Settler settler = hover != null ? hover.GetComponentInParent<Settler>() : null;
            if (!Commandable(settler, player, range))
            {
                Character creature = player.GetHoverCreature();
                settler = creature != null ? creature.GetComponent<Settler>() : null;
            }
            if (Commandable(settler, player, range))
            {
                return settler;
            }
            if (!loose || GameCamera.instance == null)
            {
                return null;
            }

            Transform camera = GameCamera.instance.transform;
            Settler best = null;
            float bestAngle = LooseAimDegrees;
            foreach (Settler candidate in Settler.Loaded)
            {
                Character body = Commandable(candidate, player, range) ? candidate.GetComponent<Character>() : null;
                if (body == null)
                {
                    continue;
                }
                Vector3 center = body.GetCenterPoint();
                float angle = Vector3.Angle(camera.forward, center - camera.position);
                if (angle < bestAngle && InSight(camera.position, center))
                {
                    best = candidate;
                    bestAngle = angle;
                }
            }
            return best;
        }

        private static bool Commandable(Settler settler, Player player, float range) =>
            settler != null && settler.Identity != null && !settler.IsCaptive &&
            Vector3.Distance(settler.transform.position, player.transform.position) <= range;

        // The creature under the crosshair, else the one closest to the crosshair inside a cone ahead, in sight.
        private static Character LookedAtEnemy(Player player)
        {
            float range = AoJConfig.CommandLookRange.Value;
            Character hovered = player.GetHoverCreature();
            if (IsValidEnemy(hovered) && Vector3.Distance(hovered.transform.position, player.transform.position) <= range)
            {
                return hovered;
            }

            Transform camera = GameCamera.instance != null ? GameCamera.instance.transform : null;
            Vector3 eye = camera != null ? camera.position : player.m_eye != null ? player.m_eye.position : player.transform.position;
            Vector3 look = camera != null ? camera.forward : player.GetLookDir();
            Character best = null;
            float bestAngle = EnemyAimDegrees;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (!IsValidEnemy(character) || Vector3.Distance(character.transform.position, player.transform.position) > range)
                {
                    continue;
                }
                Vector3 center = character.GetCenterPoint();
                float angle = Vector3.Angle(look, center - eye);
                if (angle < bestAngle && InSight(eye, center))
                {
                    best = character;
                    bestAngle = angle;
                }
            }
            return best;
        }

        // The enemy closest to a settler; animals only when no monster is around.
        private static Character NearestEnemy(Vector3 position)
        {
            Character best = null;
            float bestScore = float.MaxValue;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (!IsValidEnemy(character))
                {
                    continue;
                }
                float distance = Vector3.Distance(character.transform.position, position);
                float score = distance + (character.m_faction == Character.Faction.AnimalsVeg ? SettlerAttackRange : 0f);
                if (distance <= SettlerAttackRange && score < bestScore)
                {
                    best = character;
                    bestScore = score;
                }
            }
            return best;
        }

        // Players and tamed creatures are never targets (the settlers' AI refuses them anyway).
        private static bool IsValidEnemy(Character character) =>
            character != null && !character.IsPlayer() && !character.IsTamed() && !character.IsDead() && character.GetComponent<Settler>() == null;

        // Terrain, rocks, trees and buildings block the view; bushes, banners and creatures do not.
        private static bool InSight(Vector3 from, Vector3 to)
        {
            if (s_sightMask == 0)
            {
                s_sightMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain", "vehicle");
            }
            return !Physics.Linecast(from, to, s_sightMask, QueryTriggerInteraction.Ignore);
        }

        private static int DistanceTo(Vector3 position)
        {
            Player player = Player.m_localPlayer;
            return player != null ? Mathf.RoundToInt(Vector3.Distance(player.transform.position, position)) : 0;
        }

        private static Sprite GestureSprite(Emotes emote) =>
            RadialData.SO != null && RadialData.SO.EmoteMappings != null ? RadialData.SO.EmoteMappings.GetMapping(emote).Sprite : null;

        private static string Localize(string text, params string[] words) =>
            Localization.instance != null ? Localization.instance.Localize(text, words) : text;

        // ---------------------------------------------------------------- the wheel key

        // Jotunn renames the button to "name!guid" when it registers it, so the name is read back from its config.
        private static bool KeyDown() => s_key != null && ZInput.GetButtonDown(s_key.Name);

        private static bool KeyHeld() => s_key != null && ZInput.GetButton(s_key.Name);

        private static bool KeyReleasedAfterHold() =>
            s_key != null && ZInput.GetButtonLastPressedTimer(s_key.Name) > RadialData.SO.HoldCloseDelay && ZInput.GetButtonUp(s_key.Name);

        // ---------------------------------------------------------------- hooks into the game's radial menu

        /// <summary>The wheel key opens the radial menu the way G and T do, with the same checks.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CheckKeyboardRadialPressed))]
        private static class WheelKeyPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (!__result && KeyDown())
                {
                    __result = true;
                }
            }
        }

        /// <summary>
        /// Holding the wheel key and letting go picks the element under the cursor, pressing it again closes the wheel -
        /// like G. The game sets these controls every time the radial menu opens.
        /// </summary>
        [HarmonyPatch(typeof(RadialConfigHelper), nameof(RadialConfigHelper.SetItemInteractionControls))]
        private static class WheelKeyControlsPatch
        {
            [HarmonyPostfix]
            private static void Postfix(RadialBase radial)
            {
                Func<bool> releaseToUse = radial.GetReleaseToUse;
                Func<bool> close = radial.GetClose;
                radial.GetReleaseToUse = () => (releaseToUse != null && releaseToUse()) ||
                                               (RadialData.SO.EnableReleaseToUseMode && KeyReleasedAfterHold());
                radial.GetClose = () => (close != null && close()) || KeyDown() ||
                                        (!RadialData.SO.EnableReleaseToUseMode && KeyReleasedAfterHold());
            }
        }

        /// <summary>
        /// Which wheel opens: the wheel key gives the orders of the settler looked at (loose aim), else the squad's
        /// with the creature looked at as the target; G gives a settler's orders when the crosshair is on it, with a
        /// way back to the game's own wheel.
        /// </summary>
        [HarmonyPatch(typeof(OpenRadialConfig), nameof(OpenRadialConfig.TryOpenNonDefaultRadials))]
        private static class OpenWheelPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(RadialBase radial, ref bool __result)
            {
                Player player = Player.m_localPlayer;
                if (player == null || radial == null || RadialData.SO == null)
                {
                    return true;
                }
                bool wheelKey = KeyHeld();
                Settler settler = LookedAtSettler(player, loose: wheelKey);
                if (settler != null)
                {
                    radial.Open(new CommandWheel(settler, null), wheelKey ? null : RadialData.SO.MainGroupConfig);
                }
                else if (wheelKey)
                {
                    radial.Open(new CommandWheel(null, LookedAtEnemy(player)));
                }
                else
                {
                    return true;
                }
                __result = true;
                return false;
            }
        }

        /// <summary>The main wheel gets a "Commands" group for the squad, next to the emotes.</summary>
        [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.ConstructRadial))]
        private static class MainWheelPatch
        {
            [HarmonyPrefix]
            private static void Prefix(RadialBase __instance, List<RadialMenuElement> elements)
            {
                Player player = Player.m_localPlayer;
                if (elements == null || !(__instance.CurrentConfig is ValheimRadialConfig) || player == null ||
                    RadialData.SO == null || RadialData.SO.GroupElement == null)
                {
                    return;
                }
                GroupElement group = Object.Instantiate(RadialData.SO.GroupElement);
                group.Init(new CommandWheel(null, LookedAtEnemy(player)), __instance.CurrentConfig, __instance);
                elements.Insert(Mathf.Min(MainWheelSlot, elements.Count), group);
            }
        }
    }
}
