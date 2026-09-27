namespace AgeOfJarls.Net
{
    internal static class Peers
    {
        /// <summary>
        /// The loaded player an RPC came from. RPC senders are peer IDs, and each player's character ZDO is owned by
        /// that player's peer, so this identity comes from the network, never from what a package claims.
        /// </summary>
        internal static Player FindPlayer(long peer)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                ZNetView nview = player.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && nview.GetZDO().GetOwner() == peer)
                {
                    return player;
                }
            }
            return null;
        }
    }
}
