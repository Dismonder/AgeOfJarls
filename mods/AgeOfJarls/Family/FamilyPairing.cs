namespace AgeOfJarls.Family
{
    /// <summary>Which settlers may court each other (Family/Pairing, the server's value applies to everyone).</summary>
    public enum FamilyPairing
    {
        /// <summary>A man and a woman.</summary>
        OppositeSex,
        /// <summary>Any two adults; a couple of two women can have children (one of them carries), two men cannot.</summary>
        Any,
    }
}
