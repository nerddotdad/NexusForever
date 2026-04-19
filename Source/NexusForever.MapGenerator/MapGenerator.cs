using System;
using System.IO;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.MapGenerator.GameTable;
using NexusForever.Shared;
using NLog;

namespace NexusForever.MapGenerator
{
    internal static class MapGenerator
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static ParserResult<Parameters> parserResult;

        #if DEBUG
        private const string Title = "NexusForever: Map Generator (DEBUG)";
        #else
        private const string Title = "NexusForever: Map Generator (RELEASE)";
        #endif

        private static void Main(string[] args)
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<ArchiveManager>();
            services.AddSingleton<GameTableManager>();
            services.AddSingleton<ExtractionManager>();
            services.AddSingleton<GenerationManager>();

            LegacyServiceProvider.Provider = services.BuildServiceProvider();

            Console.Title = Title;

            parserResult = Parser.Default.ParseArguments<Parameters>(args);
            parserResult.WithParsed(ParameterOk);

            log.Info("Finished!");
        }

        private static void ParameterOk(Parameters parameters)
        {
            parameters.PatchPath = ExpandUserPath(parameters.PatchPath);
            if (!string.IsNullOrEmpty(parameters.OutputDir))
                parameters.OutputDir = ExpandUserPath(parameters.OutputDir);

            if (!Directory.Exists(parameters.PatchPath))
                throw new DirectoryNotFoundException($"Patch path was not found: {parameters.PatchPath}");

            if (!parameters.Extract && !parameters.Generate)
            {
                log.Warn("Please specify the Extract or Generate parameter");
                log.Info(GetHelp());
                return;
            }

            if ((parameters.Extract || parameters.Generate) && !string.IsNullOrEmpty(parameters.OutputDir))
            {
                parameters.OutputDir = Path.GetFullPath(parameters.OutputDir);
                Directory.CreateDirectory(parameters.OutputDir);
            }

            ArchiveManager.Instance.Initialise(parameters.PatchPath);
            GameTableManager.Instance.Initialise();

            if (parameters.Extract)
                ExtractionManager.Instance.Initialise(parameters.OutputDir);
            if (parameters.Generate)
            {
                GenerationManager.Instance.Initialise(parameters.OutputDir);

                var start = DateTime.UtcNow;
                if (parameters.WorldId.HasValue)
                    GenerationManager.Instance.GenerateWorld(parameters.WorldId.Value, parameters.GridX, parameters.GridY);
                else
                    GenerationManager.Instance.GenerateWorlds(true);

                TimeSpan span = DateTime.UtcNow - start;
                log.Info($"Generated base maps in {span.TotalSeconds}s.");
            }
        }

        private static string GetHelp()
        {
            return HelpText.AutoBuild(parserResult, h => h, e => e);
        }

        /// <summary>
        /// Expands a leading <c>~</c> to the user profile so paths work when the shell does not expand them (e.g. quoted args in fish/bash).
        /// </summary>
        private static string ExpandUserPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            path = path.Trim();
            if (path == "~")
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string tail = path.Length > 2 ? path[2..] : string.Empty;
                tail = tail.Replace('\\', Path.DirectorySeparatorChar);
                return Path.Combine(home, tail);
            }

            return path;
        }
    }
}
