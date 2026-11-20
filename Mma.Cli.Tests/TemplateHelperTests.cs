using Mma.Cli.Shared.Helpers;
using System.IO;
using Xunit;

namespace Mma.Cli.Tests
{
    public class TemplateHelperTests
    {
        [Fact]
        public void GetTemplate_ShouldReturnDefault_WhenFileDoesNotExist()
        {
            // Arrange
            var solutionPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(solutionPath);
            var defaultTemplate = "default content";

            // Act
            var result = TemplateHelper.GetTemplate(solutionPath, "AutoMapper", "NonExistent", defaultTemplate);

            // Assert
            Assert.Equal(defaultTemplate, result);

            // Cleanup
            Directory.Delete(solutionPath, true);
        }

        [Fact]
        public void GetTemplate_ShouldReturnExternal_WhenFileExists()
        {
            // Arrange
            var solutionPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var templateDir = Path.Combine(solutionPath, ".mma", "templates", "AutoMapper");
            Directory.CreateDirectory(templateDir);
            
            var externalContent = "external content";
            File.WriteAllText(Path.Combine(templateDir, "Entity.cs"), externalContent);

            // Act
            var result = TemplateHelper.GetTemplate(solutionPath, "AutoMapper", "Entity", "default content");

            // Assert
            Assert.Equal(externalContent, result);

            // Cleanup
            Directory.Delete(solutionPath, true);
        }
    }
}
