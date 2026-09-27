using System.Linq;
using AgeOfJarls.Settlement;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>The settlement blob in the Jarl's Table ZDO: what worlds already hold must keep loading.</summary>
    public class SettlementDataTests
    {
        private const long Founder = 101;
        private const long Friend = 202;

        private static SettlementData Sample()
        {
            var data = new SettlementData { Name = "Birka", Tier = 3 };
            data.Members.Add(new SettlementMember { PlayerId = Founder, Name = "Ragnar", Role = SettlementRole.Jarl });
            data.Members.Add(new SettlementMember { PlayerId = Friend, Name = "Lagertha", Role = SettlementRole.Hersir });
            data.Settlers.Add(new RosterEntry { Uid = 11, Name = "Tove", HasBed = true, BedPosition = new Vector3(1.5f, 2f, -3.25f) });
            data.Settlers.Add(new RosterEntry { Uid = 12, Name = "Eir" });
            return data;
        }

        [Fact]
        public void RoundTripKeepsEverything()
        {
            SettlementData copy = SettlementData.Deserialize(Sample().Serialize());

            Assert.NotNull(copy);
            Assert.False(copy.NeedsRewrite);
            Assert.Equal("Birka", copy.Name);
            Assert.Equal(3, copy.Tier);
            Assert.Equal(new[] { "Ragnar", "Lagertha" }, copy.Members.Select(m => m.Name));
            Assert.Equal(new[] { SettlementRole.Jarl, SettlementRole.Hersir }, copy.Members.Select(m => m.Role));
            Assert.Equal(new long[] { 11, 12 }, copy.Settlers.Select(s => s.Uid));
            Assert.True(copy.Settlers[0].HasBed);
            Assert.Equal(new Vector3(1.5f, 2f, -3.25f), copy.Settlers[0].BedPosition);
            Assert.False(copy.Settlers[1].HasBed);
        }

        // Format 4 (mod 0.2) saved Hersir = 1 and Jarl = 2; format 5 added Karl and Huskarl below the Hersir.
        [Fact]
        public void Format4RanksAreMappedAndRewritten()
        {
            var package = new ZPackage();
            package.Write(4);
            package.Write("Old");
            package.Write(1);
            package.Write(2);
            WriteMember(package, Founder, "Ragnar", 2);
            WriteMember(package, Friend, "Lagertha", 1);
            package.Write(1);
            package.Write(11L);
            package.Write("Tove");
            package.Write(true);
            package.Write(new Vector3(1f, 2f, 3f));

            SettlementData data = SettlementData.Deserialize(package.GetArray());

            Assert.NotNull(data);
            Assert.True(data.NeedsRewrite);
            Assert.Equal(SettlementRole.Jarl, data.RoleOf(Founder));
            Assert.Equal(SettlementRole.Hersir, data.RoleOf(Friend));
            Assert.Single(data.Settlers);
            SettlementData again = SettlementData.Deserialize(data.Serialize());
            Assert.False(again.NeedsRewrite);
            Assert.Equal(SettlementRole.Hersir, again.RoleOf(Friend));
        }

        // Formats 2-3 listed settlers by ZDOID, which the game renumbers on every load: the members stay, the roster goes.
        [Fact]
        public void Format3RosterIsDroppedButMembersStay()
        {
            var package = new ZPackage();
            package.Write(3);
            package.Write("Older");
            package.Write(0);
            package.Write(1);
            WriteMember(package, Founder, "Ragnar", 2);
            package.Write(2);

            SettlementData data = SettlementData.Deserialize(package.GetArray());

            Assert.NotNull(data);
            Assert.Equal(SettlementRole.Jarl, data.RoleOf(Founder));
            Assert.Empty(data.Settlers);
        }

        [Fact]
        public void NewerFormatIsLeftAlone()
        {
            var package = new ZPackage();
            package.Write(99);
            package.Write("Future");

            Assert.Null(SettlementData.Deserialize(package.GetArray()));
        }

        [Fact]
        public void TruncatedDataIsLeftAlone()
        {
            byte[] blob = Sample().Serialize();

            Assert.Null(SettlementData.Deserialize(blob.Take(blob.Length / 2).ToArray()));
        }

        [Fact]
        public void UnknownRankIsDroppedAndRewritten()
        {
            var package = new ZPackage();
            package.Write(5);
            package.Write("Odd");
            package.Write(0);
            package.Write(2);
            WriteMember(package, Founder, "Ragnar", (int)SettlementRole.Jarl);
            WriteMember(package, Friend, "Nobody", 9);
            package.Write(0);

            SettlementData data = SettlementData.Deserialize(package.GetArray());

            Assert.NotNull(data);
            Assert.True(data.NeedsRewrite);
            Assert.Single(data.Members);
            Assert.Equal(SettlementRole.Guest, data.RoleOf(Friend));
        }

        [Fact]
        public void RosterRespectsCapacityAndDuplicates()
        {
            var data = new SettlementData();

            Assert.True(data.TryAddSettler(11, "Tove", 2));
            Assert.True(data.TryAddSettler(12, "Eir", 2));
            Assert.False(data.TryAddSettler(13, "Ulf", 2));
            Assert.False(data.TryAddSettler(11, "Tove again", 3));
            Assert.False(data.TryAddSettler(0, "No id", 3));
            Assert.True(data.RemoveSettler(11));
            Assert.False(data.HasSettler(11));
        }

        private static void WriteMember(ZPackage package, long id, string name, int rank)
        {
            package.Write(id);
            package.Write(name);
            package.Write(rank);
        }
    }
}
