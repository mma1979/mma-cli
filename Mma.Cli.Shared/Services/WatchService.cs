using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mma.Cli.Shared.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Services
{
    public class WatchService
    {
        private readonly FileSystemWatcher _watcher;
        private readonly string _solutionPath;
        private readonly string _projectPath;

        public WatchService(string solutionPath)
        {
            _solutionPath = solutionPath;
            (_, _projectPath) = BuildHelper.CheckSolutionPath(solutionPath);
            var pathToWatch = Path.Combine(_projectPath, $"{BuildHelper.GetSolutionName(solutionPath)}.Core", "Database", "Tables");
            _watcher = new FileSystemWatcher(pathToWatch)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                Filter = "*.cs",
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
        }

        public void Start()
        {
            Output.Warning("Starting file watcher...");
            while (true)
            {
                Thread.Sleep(1000);
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                Output.Warning($"File changed: {e.Name}");
                UpdateModels(e.FullPath);
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Output.Warning($"File renamed: {e.OldName} to {e.Name}");
            // Handle rename - maybe delete old models and create new ones
        }

        private void UpdateModels(string entityFilePath)
        {
            var entityName = Path.GetFileNameWithoutExtension(entityFilePath);
            var solutionName = BuildHelper.GetSolutionName(_solutionPath);
            var modelsPath = Path.Combine(_projectPath, $"{solutionName}.Core", "Models");
            var readModelPath = Path.Combine(modelsPath, $"{entityName}ReadModel.cs");
            var modifyModelPath = Path.Combine(modelsPath, $"{entityName}ModifyModel.cs");

            var entitySyntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(entityFilePath));
            var entityRoot = entitySyntaxTree.GetCompilationUnitRoot();
            var classDeclaration = entityRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            var properties = classDeclaration.DescendantNodes().OfType<PropertyDeclarationSyntax>().ToList();

            UpdateModel(readModelPath, properties);
            UpdateModel(modifyModelPath, properties);
        }

        private void UpdateModel(string modelPath, List<PropertyDeclarationSyntax> properties)
        {
            if (!File.Exists(modelPath))
            {
                // Create the model file if it doesn't exist
                // For now, we'll just log it.
                Output.Warning($"Model file not found: {modelPath}");
                return;
            }

            var modelSyntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(modelPath));
            var modelRoot = modelSyntaxTree.GetCompilationUnitRoot();
            var modelClass = modelRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().First();

            var existingProperties = modelClass.DescendantNodes().OfType<PropertyDeclarationSyntax>().ToList();
            var newProperties = new List<PropertyDeclarationSyntax>();

            foreach (var property in properties)
            {
                var existingProp = existingProperties.FirstOrDefault(p => p.Identifier.ValueText == property.Identifier.ValueText);
                if (existingProp == null)
                {
                    newProperties.Add(property);
                }
            }

            if (newProperties.Any())
            {
                var newClass = modelClass.AddMembers(newProperties.ToArray());
                var newRoot = modelRoot.ReplaceNode(modelClass, newClass);
                File.WriteAllText(modelPath, newRoot.ToFullString());
                Output.Success($"Updated model: {Path.GetFileName(modelPath)}");
            }
        }
    }
}
