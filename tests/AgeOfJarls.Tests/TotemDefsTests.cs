using System.Linq;
using AgeOfJarls.Core.Defs;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>totems.json from a file or from the server: a bad entry must never block loading or break upgrades.</summary>
    public class TotemDefsTests
    {
        private static TotemLevelDef Level(int level, float pace = 0.1f, int places = 1, params (string item, int amount)[] cost) =>
            new TotemLevelDef
            {
                Level = level,
                PaceBonus = pace,
                ExtraPlaces = places,
                Cost = cost.Select(c => new TierCost { Item = c.item, Amount = c.amount }).ToList(),
            };

        private static TotemsDef Validate(params TotemLevelDef[] levels) =>
            DefsValidator.Totems(new TotemsDef { Levels = levels.ToList() }, "test");

        [Fact]
        public void LevelsAreSortedFromTwo()
        {
            Assert.Equal(new[] { 2, 3 }, Validate(Level(3), Level(2)).Levels.Select(l => l.Level));
        }

        [Fact]
        public void GapEndsTheList()
        {
            Assert.Single(Validate(Level(2), Level(4)).Levels);
        }

        [Fact]
        public void WithoutLevelTwoTotemsCannotBeUpgraded()
        {
            Assert.Empty(Validate(Level(3)).Levels);
        }

        [Fact]
        public void MissingDataGivesNoUpgradesInsteadOfNull()
        {
            Assert.Empty(DefsValidator.Totems(null, "test").Levels);
            Assert.Empty(DefsValidator.Totems(new TotemsDef { Levels = null }, "test").Levels);
        }

        [Fact]
        public void BonusesAreClamped()
        {
            TotemLevelDef high = Validate(Level(2, pace: 7f, places: 99)).Levels[0];
            Assert.Equal(1f, high.PaceBonus);
            Assert.Equal(5, high.ExtraPlaces);

            TotemLevelDef low = Validate(Level(2, pace: -1f, places: -3)).Levels[0];
            Assert.Equal(0f, low.PaceBonus);
            Assert.Equal(0, low.ExtraPlaces);
        }

        [Fact]
        public void BadCostEntriesAreSkipped()
        {
            TotemLevelDef level = Validate(Level(2, 0.1f, 1, ("Wood", 20), ("", 5), ("Stone", 0), (" Resin ", 5))).Levels[0];

            Assert.Equal(new[] { "Wood", "Resin" }, level.Cost.Select(c => c.Item));
        }

        [Fact]
        public void AtMostFiveLevels()
        {
            TotemLevelDef[] levels = Enumerable.Range(2, 7).Select(l => Level(l)).ToArray();

            Assert.Equal(DefsValidator.MaxTotemLevel - 1, Validate(levels).Levels.Count);
        }
    }
}
