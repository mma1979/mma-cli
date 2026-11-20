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
        public ImportFactory()
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
        }

        public static ImportFactory New(string[] args)
        {
            var factory = new ImportFactory()
            {
                SolutionPath = args[1],
            };

            (factory.SolutionName, factory.ProjectsPath) = BuildHelper.CheckSolutionPath(factory.SolutionPath);

            return factory;
        }

        public ImportFactory Import()
        {
            var mmaProjectPah = Path.Combine(SolutionPath, ".mma", "project.mma");
            var json = File.ReadAllText(mmaProjectPah, Encoding.UTF8);
            var body = JsonConvert.DeserializeObject<ProjectDumpModel>(json)!;

            var baseUrl = ConfigHelper.GetSetting(SolutionPath, "GeneratorEndpoint", "https://localhost:44357");
            var client = new RestClient(baseUrl);
            var request = new RestRequest("api/projects/import", Method.Post);
            request.RequestFormat = DataFormat.Json;
            request.AddJsonBody(body, ContentType.Json);
            var response = client.Execute(request);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine(response.ErrorException);
                Output.Error("Faild to Export Project");
                return this;
            }

            Output.Success("Project has exported successfuly");
            return this;
        }

        


    }
}
