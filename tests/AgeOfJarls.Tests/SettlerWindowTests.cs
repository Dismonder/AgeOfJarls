using AgeOfJarls.UI;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>Defensive inventory rendering for saved items whose prefab icons have changed.</summary>
    public class SettlerWindowTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, -1)]
        [InlineData(1, 1)]
        [InlineData(2, 10)]
        [InlineData(2, int.MaxValue)]
        public void InvalidIconVariantsRenderWithoutAnIcon(int iconCount, int variant)
        {
            var item = new ItemDrop.ItemData
            {
                m_shared = new ItemDrop.ItemData.SharedData { m_icons = new Sprite[iconCount] },
                m_variant = variant,
            };
            Assert.Null((object)SettlerWindow.ItemIcon(item));
        }

        [Fact]
        public void MissingIconDataRendersWithoutAnIcon()
        {
            Assert.Null((object)SettlerWindow.ItemIcon(null));
            Assert.Null((object)SettlerWindow.ItemIcon(new ItemDrop.ItemData { m_shared = null }));
            Assert.Null((object)SettlerWindow.ItemIcon(new ItemDrop.ItemData
            {
                m_shared = new ItemDrop.ItemData.SharedData { m_icons = null },
            }));
        }

        [Fact]
        public void ValidVariantWithAMissingSpriteRendersWithoutAnIcon()
        {
            var item = new ItemDrop.ItemData
            {
                m_shared = new ItemDrop.ItemData.SharedData { m_icons = new Sprite[2] },
                m_variant = 1,
            };
            Assert.Null((object)SettlerWindow.ItemIcon(item));
        }
    }
}
