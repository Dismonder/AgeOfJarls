namespace AgeOfJarls.Settlement
{
    /// <summary>What a rank in a settlement allows (the ranks themselves are handed out by <see cref="SettlementData.RankChangeProblem"/>).</summary>
    internal enum SettlementRight
    {
        /// <summary>Bringing your followers in, everyday orders to its settlers (home, name, handing items back), feasts, the map pin.</summary>
        Live,
        /// <summary>The alarm, combat roles, war banners, battle orders to settlers that do not follow you.</summary>
        Military,
        /// <summary>Work and totems, dismissing settlers, the settlement's name and tier.</summary>
        Manage,
        /// <summary>Taking down the Jarl's Table, and with it the settlement.</summary>
        Rule,
    }

    /// <summary>
    /// The one table of rights every owner checks - the table's, the settlers', the totems' and the banners' - with
    /// the requester's rank taken from the network identity: Karl lives there, Huskarl leads the troop, Hersir runs
    /// the settlement, Jarl rules it.
    /// </summary>
    internal static class Permissions
    {
        internal static SettlementRole Required(SettlementRight right)
        {
            switch (right)
            {
                case SettlementRight.Live:
                    return SettlementRole.Karl;
                case SettlementRight.Military:
                    return SettlementRole.Huskarl;
                case SettlementRight.Manage:
                    return SettlementRole.Hersir;
                default:
                    return SettlementRole.Jarl;
            }
        }

        internal static bool Allows(this SettlementRole role, SettlementRight right) => role >= Required(right);

        /// <summary>Localization token of a rank, e.g. "$aoj_role_hersir".</summary>
        internal static string Token(SettlementRole role) => "$aoj_role_" + role.ToString().ToLowerInvariant();

        /// <summary>"Needs the rank: Hersir", localized, for a refused action.</summary>
        internal static string Denied(SettlementRight right) =>
            Localization.instance != null
                ? Localization.instance.Localize("$aoj_msg_needs_rank", Localization.instance.Localize(Token(Required(right))))
                : "$aoj_msg_needs_rank";
    }
}
