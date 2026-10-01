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

        /// <summary>Peer id of the server: this machine on the host, the server on a client; 0 while not connected.</summary>
        internal static long ServerPeerId()
        {
            ZNet net = ZNet.instance;
            if (net == null)
            {
                return 0L;
            }
            if (net.IsServer())
            {
                return ZDOMan.GetSessionID();
            }
            ZNetPeer server = net.GetServerPeer();
            return server != null ? server.m_uid : 0L;
        }
    }
}
