using System.Collections.Generic;
using AgeOfJarls.Core.Defs;
using AgeOfJarls.Settlers;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>The pure parts of a child's roll: the name avoids the roster, the traits come from the parents within the rules.</summary>
    public class SettlerRollerTests
    {
        private static TraitDef Trait(string id, bool positive = true, params string[] excludes) =>
            new TraitDef { Id = id, Positive = positive, Weight = 1f, Excludes = new List<string>(excludes) };

        private static List<TraitDef> Pool() => new List<TraitDef>
        {
            Trait("strong", true, "weak"),
            Trait("weak", false, "strong"),
            Trait("cheerful"),
            Trait("lazy", false),
        };

        [Fact]
        public void ChildNameAvoidsNamesAlreadyOnTheRoster()
        {
            var names = new List<string> { "Astrid", "Brynhild", "Dagny" };
            var taken = new HashSet<string> { "Astrid", "Dagny" };
            for (int seed = 0; seed < 20; seed++)
            {
                Assert.Equal("Brynhild", SettlerRoller.PickChildName(new System.Random(seed), names, taken));
            }
        }

        [Fact]
        public void ChildNameFallsBackToAnyNameWhenAllAreTaken()
        {
            var names = new List<string> { "Astrid", "Brynhild" };
            var taken = new HashSet<string>(names);
            Assert.Contains(SettlerRoller.PickChildName(new System.Random(1), names, taken), names);
            Assert.Equal("Viking", SettlerRoller.PickChildName(new System.Random(1), new List<string>(), taken));
        }

        [Fact]
        public void InheritedTraitsHonourExclusionsAndTheLimit()
        {
            var mother = new List<string> { "strong", "cheerful" };
            var father = new List<string> { "weak", "lazy" };
            for (int seed = 0; seed < 50; seed++)
            {
                List<string> traits = SettlerRoller.InheritTraits(new System.Random(seed), mother, father, Pool(), 2);
                Assert.InRange(traits.Count, 1, 2);
                Assert.False(traits.Contains("strong") && traits.Contains("weak"), "excluded pair inherited together");
                Assert.Equal(traits.Count, new HashSet<string>(traits).Count);
            }
        }

        [Fact]
        public void InheritedTraitsComeOnlyFromParentsOrOnePositiveFallback()
        {
            var mother = new List<string> { "lazy" };
            List<TraitDef> pool = Pool();
            bool sawFallback = false;
            for (int seed = 0; seed < 50; seed++)
            {
                List<string> traits = SettlerRoller.InheritTraits(new System.Random(seed), mother, null, pool, 3);
                Assert.Single(traits);
                if (traits[0] != "lazy")
                {
                    // Nothing inherited: one random positive trait instead.
                    sawFallback = true;
                    Assert.True(pool.Find(t => t.Id == traits[0]).Positive);
                }
            }
            Assert.True(sawFallback);
        }

        [Fact]
        public void TraitsNoLongerDefinedAreSkipped()
        {
            var mother = new List<string> { "gone" };
            List<string> traits = SettlerRoller.InheritTraits(new System.Random(3), mother, null, Pool(), 3);
            Assert.Single(traits);
            Assert.NotEqual("gone", traits[0]);
        }
    }
}
