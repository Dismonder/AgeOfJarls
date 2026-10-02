using Xunit;

namespace ModMenu.Tests
{
    public class ModSourcesTests
    {
        [Theory]
        // Archive names copied from the Vortex deployment manifest of the dev install.
        [InlineData("Auto Store 174 0.7.1 2026-09-11T02-59Z uwYaWjkTr", 174, "0.7.1")]
        [InlineData("KeepAchivements 3618 1 2026-09-10T08-54Z sVRYlHWlp", 3618, "1")]
        [InlineData("DarkmoonBlade's Valheim Achievements Enabler 3611 1.1 2026-09-09T19-01Z vPxq6Dhed", 3611, "1.1")]
        [InlineData("FloatingItems-241-1-0-0-1614726408", 241, "1.0.0")]
        // A number inside the mod's own name must not be taken for the id.
        [InlineData("Valheim Plus 2 123 1.0 2026-01-01T00-00Z abc", 123, "1.0")]
        public void ParsesVortexArchiveNames(string archive, int id, string version)
        {
            Assert.True(ModSources.ParseVortexArchive(archive, out int parsedId, out string parsedVersion));
            Assert.Equal(id, parsedId);
            Assert.Equal(version, parsedVersion);
        }

        [Theory]
        [InlineData("SomeMod.zip")]
        [InlineData("")]
        [InlineData(null)]
        public void RejectsUnknownArchiveNames(string archive)
        {
            Assert.False(ModSources.ParseVortexArchive(archive, out _, out _));
        }

        [Theory]
        [InlineData("2.30.2", "2.30.0", true)]
        [InlineData("1.7.1", "1", true)]
        [InlineData("1.10", "1.1", true)]
        [InlineData("1", "1.1", false)]
        [InlineData("1.0", "1", false)]
        [InlineData("3.0.0", "3.1.2", false)]
        [InlineData("v1.2.0", "1.1.9", true)]
        [InlineData("beta", "1.0", false)]
        [InlineData(null, "1.0", false)]
        public void ComparesVersions(string latest, string installed, bool newer)
        {
            Assert.Equal(newer, ModSources.IsNewer(latest, installed));
        }
    }
}
