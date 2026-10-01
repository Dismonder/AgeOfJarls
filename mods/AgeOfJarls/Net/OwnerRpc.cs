using System.Collections.Generic;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Net
{
    /// <summary>
    /// For an RPC handler that only the object's owner may carry out (a piece's settings, a settlement action, a
    /// chest's kind). The request was aimed at the owner, but ownership may have moved while it travelled - or the
    /// object may belong to nobody for a moment: its owner left the area and the server has not handed it to anybody
    /// yet. Passing it on to owner 0 is a broadcast the game handles on this machine at once, in the same handler,
    /// without end: that closed the game (0.6.0). An ownerless object is claimed instead, as the game claims a chest
    /// a player opens; a request bouncing between two machines that each believe the other owns the object is
    /// dropped after ten a second.
    /// </summary>
    internal static class OwnerRpc
    {
        private const string Module = "Net";
        private const int MaxForwardsPerSecond = 10;

        private sealed class Window
        {
            internal float End;
            internal int Count;
        }

        private static readonly Dictionary<ZDOID, Window> s_forwards = new Dictionary<ZDOID, Window>();

        /// <summary>True when this machine owns (or just took) the object and the handler should go on.</summary>
        internal static bool Handles(ZNetView nview, string method, params object[] args)
        {
            if (nview == null || !nview.IsValid())
            {
                return false;
            }
            if (nview.IsOwner())
            {
                return true;
            }
            if (!nview.HasOwner())
            {
                nview.ClaimOwnership();
                return true;
            }
            ZDOID id = nview.GetZDO().m_uid;
            if (!s_forwards.TryGetValue(id, out Window window) || Time.time > window.End)
            {
                if (s_forwards.Count > 256)
                {
                    s_forwards.Clear();
                }
                window = new Window { End = Time.time + 1f };
                s_forwards[id] = window;
            }
            if (++window.Count > MaxForwardsPerSecond)
            {
                if (window.Count == MaxForwardsPerSecond + 1)
                {
                    Log.Warning(Module, $"{method} on {nview.name} passed on to peer {nview.GetZDO().GetOwner()} too often, dropped until the owner settles");
                }
                return false;
            }
            nview.InvokeRPC(method, args);
            return false;
        }
    }
}
