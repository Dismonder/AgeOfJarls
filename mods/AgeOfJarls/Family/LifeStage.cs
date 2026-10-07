namespace AgeOfJarls.Family
{
    /// <summary>
    /// How far a settler born in a settlement has grown. Never stored: every machine derives it from the birth time
    /// (<see cref="Core.Keys.ZdoSettlerBorn"/>) and the Family/*Days config, see <see cref="FamilyRules.StageFor"/>.
    /// The order matters (comparisons) and the values are saved in child records as LastStage: only append.
    /// </summary>
    public enum LifeStage
    {
        Infant = 0,
        Child = 1,
        Youth = 2,
        Adult = 3,
    }
}
