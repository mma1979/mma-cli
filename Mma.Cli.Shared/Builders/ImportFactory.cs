using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Models;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RestSharp;

namespace Mma.Cli.Shared.Builders
{
    public class ImportFactory
    {

        public string SolutionPath { get; private set; } = "";
        public string SolutionName { get; private set; } = "";
        public string ProjectsPath { get; private set; } = "";
        private const string BaseUrl = "http://127.0.0.1:5080";

        public ImportFactory()
        {
        }

        public static ImportFactory New(string[] args)
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                throw new MmaCliException(
                    "invalid_arguments",
                    "Import command requires a solution path.",
                    exitCode: 2);
            }

            var factory = new ImportFactory()
            {
                SolutionPath = args[1],
            };

            (factory.SolutionName, factory.ProjectsPath) = BuildHelper.CheckSolutionPath(factory.SolutionPath);

            return factory;
        }

        public ImportFactory Import()
        {

            var mmaProjectPath = Path.Combine(SolutionPath, ".mma", "project.mma");
            if (!File.Exists(mmaProjectPath))
            {
                throw new MmaCliException(
                    "missing_mma_project",
                    $"Could not find '{mmaProjectPath}'.",
                    exitCode: 66);
            }

            var json = File.ReadAllText(mmaProjectPath, Encoding.UTF8);
            var body = JsonConvert.DeserializeObject<ProjectDumpModel>(json)
                ?? throw new MmaCliException("invalid_mma_project", "Failed to deserialize project.mma.", exitCode: 1);

            var client = new RestClient(BaseUrl);
            var request = new RestRequest("api/projects/import", Method.Post);
            request.RequestFormat = DataFormat.Json;
            request.AddJsonBody(body, ContentType.Json);
            var response = client.Execute(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new MmaCliException(
                    "import_failed",
                    $"Failed to import project: {response.ErrorMessage ?? response.StatusCode.ToString()}",
                    exitCode: 1,
                    response.ErrorException);
            }

            Output.Success("Project has been imported successfully");
            return this;
        }
    }
}
