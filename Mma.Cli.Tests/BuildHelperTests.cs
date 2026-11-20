using Mma.Cli.Shared.Helpers;
using Xunit;

namespace Mma.Cli.Tests
{
    public class BuildHelperTests
    {
        [Theory]
        [InlineData("User", "Users")]
        [InlineData("Category", "Categories")]
        [InlineData("Address", "Addresses")]
        [InlineData("Entity", "Entities")]
        [InlineData("Bus", "Buses")]
        public void GetSetName_ShouldPluralizeCorrectly(string input, string expected)
        {
            // Act
            var result = BuildHelper.GetSetName(input);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
