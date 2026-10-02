using BepInEx.Configuration;
using ModMenu.UI;
using Xunit;

namespace ModMenu.Tests
{
    public class ConfigParsingTests
    {
        [Theory]
        [InlineData("0,5", 0.5f)]
        [InlineData("0.5", 0.5f)]
        [InlineData(" 2,25 ", 2.25f)]
        public void AcceptsDecimalCommaAndDot(string text, float expected)
        {
            Assert.True(ConfigEditors.TryParse(text, typeof(float), null, out object value));
            Assert.Equal(expected, (float)value);
        }

        [Fact]
        public void RefusesValueOutsideAllowedList()
        {
            var allowed = new AcceptableValueList<string>("Chill", "Realistic");
            Assert.True(ConfigEditors.TryParse("Realistic", typeof(string), allowed, out _));
            Assert.False(ConfigEditors.TryParse("Hardcore", typeof(string), allowed, out _));
        }

        [Fact]
        public void LeavesRangesToBepInExClamping()
        {
            var range = new AcceptableValueRange<int>(0, 10);
            Assert.True(ConfigEditors.TryParse("50", typeof(int), range, out object value));
            Assert.Equal(50, (int)value);
        }

        [Fact]
        public void RejectsText()
        {
            Assert.False(ConfigEditors.TryParse("abc", typeof(int), null, out _));
        }
    }
}
