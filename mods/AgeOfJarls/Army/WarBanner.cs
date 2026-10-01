using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgeOfJarls.Core;
using AgeOfJarls.Net;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Army
{
    /// <summary>
    /// A War Banner: a post for the troop. Its kind says who stands there - Rally (warriors gather), Wall (archers),
    /// Gate (shieldbearers hold the breach) or Shelter (civilians hide during an alarm). Soldiers of the matching role
    /// pick the nearest banner with a free place (see <see cref="Posts"/>). [E] cycles the kind for the Jarl and the
    /// Hersirs; the change is an RPC checked by the banner's owner.
    /// </summary>
    public class WarBanner : MonoBehaviour, Hoverable, Interactable
    {
        private const string Module = "Army";
        internal const float PostRadius = 4f;
        private const float MarkerSeconds = 0.5f;

        internal static readonly List<WarBanner> Loaded = new List<WarBanner>();

        public CircleProjector m_areaMarker;

        private ZNetView _nview;
        private Piece _piece;

        internal long Id => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoBannerId) : 0L;

        internal BannerKind Kind => _nview != null && _nview.IsValid() ? (BannerKind)_nview.GetZDO().GetInt(Keys.ZdoBannerKind) : BannerKind.Rally;

        /// <summary>Soldiers a banner takes: a small squad per post.</summary>
        internal int Places => Kind == BannerKind.Shelter ? 30 : 4;

        internal JarlTable Settlement => JarlTable.FindContaining(transform.position);

        internal static WarBanner FindById(long id)
        {
            if (id == 0L)
            {
                return null;
            }
            foreach (WarBanner banner in Loaded)
            {
                if (banner.Id == id)
                {
                    return banner;
                }
            }
            return null;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            if (m_areaMarker != null)
            {
                m_areaMarker.m_radius = PostRadius;
            }
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }
            Loaded.Add(this);
            HideMarker();
            _nview.Register<int>(Keys.RpcBannerConfig, RPC_SetKind);
        }

        private void Start()
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && _nview.GetZDO().GetLong(Keys.ZdoBannerId) == 0L)
            {
                _nview.GetZDO().Set(Keys.ZdoBannerId, Keys.NewId());
            }
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
        }

        /// <summary>Loaded settlers posted here.</summary>
        internal List<Settler> Posted()
        {
            long id = Id;
            return id == 0L ? new List<Settler>() : Settler.Loaded.Where(s => s.PostId == id).ToList();
        }

        /// <summary>How many loaded settlers are posted here, without building a list (soldiers ask this often).</summary>
        internal int PostedCount()
        {
            long id = Id;
            int count = 0;
            if (id != 0L)
            {
                foreach (Settler settler in Settler.Loaded)
                {
                    if (settler != null && settler.PostId == id)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        internal bool MayManage(Player player)
        {
            if (player == null)
            {
                return false;
            }
            JarlTable table = Settlement;
            if (table != null && table.Data != null)
            {
                return table.RoleOf(player).Allows(SettlementRight.Military);
            }
            return _piece != null && _piece.GetCreator() == player.GetPlayerID();
        }

        private void RPC_SetKind(long sender, int kind)
        {
            if (!Net.OwnerRpc.Handles(_nview, Keys.RpcBannerConfig, kind))
            {
                return;
            }
            if (!MayManage(Peers.FindPlayer(sender)) || kind < 0 || kind > (int)BannerKind.Shelter)
            {
                Log.Warning(Module, $"Banner change from peer {sender} refused");
                return;
            }
            _nview.GetZDO().Set(Keys.ZdoBannerKind, kind);
        }

        public string GetHoverText()
        {
            ShowMarker();
            if (_nview == null || !_nview.IsValid())
            {
                return Localize("$aoj_piece_banner");
            }
            List<Settler> posted = Posted();
            var text = new StringBuilder();
            text.Append("<b>$aoj_piece_banner: ").Append(KindToken(Kind)).Append("</b>\n");
            text.Append("<color=#b0b0b0>").Append(KindToken(Kind)).Append("_desc</color>\n");
            text.Append("$aoj_posted: ").Append(posted.Count == 0 ? "-" : string.Join(", ", posted.Select(s => s.DisplayName)))
                .Append($" ({posted.Count}/{Places})");
            if (MayManage(Player.m_localPlayer))
            {
                text.Append("\n[<color=yellow><b>$KEY_Use</b></color>] $aoj_banner_change");
            }
            return Localize(text.ToString());
        }

        public string GetHoverName() => Localize("$aoj_piece_banner");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || _nview == null || !_nview.IsValid())
            {
                return false;
            }
            if (!MayManage(player))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Military));
                return true;
            }
            int next = ((int)Kind + 1) % ((int)BannerKind.Shelter + 1);
            _nview.InvokeRPC(Keys.RpcBannerConfig, next);
            player.Message(MessageHud.MessageType.Center, Localize(KindToken((BannerKind)next)));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        internal static string KindToken(BannerKind kind) => "$aoj_banner_" + kind.ToString().ToLowerInvariant();

        internal void ShowMarker()
        {
            if (m_areaMarker == null)
            {
                return;
            }
            m_areaMarker.gameObject.SetActive(true);
            CancelInvoke(nameof(HideMarker));
            Invoke(nameof(HideMarker), MarkerSeconds);
        }

        private void HideMarker()
        {
            if (m_areaMarker != null)
            {
                m_areaMarker.gameObject.SetActive(false);
            }
        }

        private static string Localize(string text) => Localization.instance != null ? Localization.instance.Localize(text) : text;
    }
}
