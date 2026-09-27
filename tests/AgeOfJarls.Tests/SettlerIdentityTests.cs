using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>The settler's identity blob (its ZDO) and the names players type for settlers and settlements.</summary>
    public class SettlerIdentityTests
    {
        [Fact]
        public void RoundTripKeepsEverything()
        {
            var identity = new SettlerIdentity
            {
                Name = "Tove",
                Female = true,
                Origin = Heightmap.Biome.Swamp,
                Hair = "Hair5",
                Beard = "",
                SkinColor = new Vector3(0.8f, 0.7f, 0.6f),
                HairColor = new Vector3(0.2f, 0.1f, 0.05f),
            };
            identity.Traits.Add("hardworking");
            identity.Traits.Add("frugal");

            SettlerIdentity copy = SettlerIdentity.Deserialize(identity.Serialize());

            Assert.NotNull(copy);
            Assert.Equal("Tove", copy.Name);
            Assert.True(copy.Female);
            // As numbers: a generic Assert over the game's Biome type would load Heightmap, whose interfaces use
            // default methods that .NET Framework (the test runner) cannot load; Unity's runtime can.
            Assert.Equal((int)Heightmap.Biome.Swamp, (int)copy.Origin);
            Assert.Equal(new[] { "hardworking", "frugal" }, copy.Traits);
            Assert.Equal("Hair5", copy.Hair);
            Assert.Equal(new Vector3(0.8f, 0.7f, 0.6f), copy.SkinColor);
            Assert.Equal(new Vector3(0.2f, 0.1f, 0.05f), copy.HairColor);
        }

        [Fact]
        public void UnknownFormatIsLeftAlone()
        {
            var package = new ZPackage();
            package.Write(2);
            package.Write("From a newer version");

            Assert.Null(SettlerIdentity.Deserialize(package.GetArray()));
        }

        [Fact]
        public void TruncatedDataIsLeftAlone()
        {
            byte[] blob = new SettlerIdentity { Name = "Eir" }.Serialize();

            Assert.Null(SettlerIdentity.Deserialize(new[] { blob[0], blob[1] }));
        }

        [Theory]
        [InlineData("  Tove  ", "Tove")]
        [InlineData("<b>Ulf</b>", "bUlf/b")]
        [InlineData("$aoj_jarl", "aoj_jarl")]
        [InlineData("Line\nbreak", "Linebreak")]
        [InlineData(null, "")]
        public void NamesAreCleaned(string typed, string expected)
        {
            Assert.Equal(expected, TextUtil.SanitizeName(typed, 32));
        }

        [Fact]
        public void NamesAreCutToTheLimit()
        {
            Assert.Equal("Ragnar", TextUtil.SanitizeName("Ragnar Lothbrok", 7));
        }
    }
}
