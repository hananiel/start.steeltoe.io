﻿// Copyright 2017 the original author or authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Steeltoe.Initializr.Models;
using Steeltoe.Initializr.Services.DotNetTemplateEngine;
using Stubble.Core;
using Stubble.Core.Builders;
using Stubble.Extensions.JsonNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Steeltoe.Initializr.Services.Mustache
{
    /// <summary>
    /// An implementation of a Dotnet Template service via Mustache (Stubble).
    /// </summary>
    public class MustacheTemplateService : ITemplateService
    {
        private const string DefaultTemplateName = "CSharp-WebApi-2.x";

        private Dictionary<string, string> FriendlyNames { get; set; }

        private readonly StubbleVisitorRenderer _stubble;
        private readonly ILogger<MustacheTemplateService> _logger;
        private readonly string _templatePath;
        private readonly MustacheConfig _mustacheConfig;

        public MustacheTemplateService(IConfiguration configuration, ILogger<MustacheTemplateService> logger)
            : this(logger)
        {
            configuration.Bind(this); // Get friendlyNames
        }

        public MustacheTemplateService(ILogger<MustacheTemplateService> logger)
        {
            _stubble = new StubbleBuilder()
                .Configure(settings => settings.AddJsonNet())
                .Build();
            _logger = logger;
            _templatePath = AppDomain.CurrentDomain.BaseDirectory + "templates" + Path.DirectorySeparatorChar +
                            "Mustache";
            _mustacheConfig = new MustacheConfig();
        }

        public async Task<byte[]> GenerateProjectArchiveAsync(GeneratorModel model)
        {
            return await Task.Run(() => GenerateProjectArchive(model)).ConfigureAwait(false);
        }

        public List<KeyValuePair<string, string>> GenerateProjectFiles(GeneratorModel model)
        {
            var name = string.IsNullOrEmpty(model.TemplateShortName) ? DefaultTemplateName : model.TemplateShortName;

            var templatePath = _templatePath + Path.DirectorySeparatorChar + name;

            if (!Directory.Exists(templatePath))
            {
                throw new InvalidDataException("Template with $name doesnt exist");
            }

            var dataView = _mustacheConfig.GetDataView(templatePath, model);
            var listOfFiles = new List<KeyValuePair<string, string>>();

            foreach (var file in _mustacheConfig.GetFilteredSourceSets(dataView, templatePath))
            {
                if (file.EndsWith("mustache.json")
                    || file.EndsWith("sourceExclusions.json"))
                {
                    continue;
                }

                var pathPrefix = file.Replace(Path.GetFullPath(templatePath), string.Empty)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string fileText = File.ReadAllText(file);

                if (file.EndsWith(".csproj"))
                {
                    pathPrefix =
                        pathPrefix.Replace("ReplaceMe", model.ProjectName ?? "SteeltoeExample"); // get from model
                    var output = Render(file, fileText, dataView);
                    listOfFiles.Add(new KeyValuePair<string, string>(pathPrefix, output));
                }
                else
                {
                    var output = Render(file, fileText, dataView);
                    listOfFiles.Add(new KeyValuePair<string, string>(pathPrefix, output));
                }
            }

            return listOfFiles;
        }

        public List<TemplateViewModel> GetAvailableTemplates()
        {
            return Directory
                .GetDirectories(_templatePath)
                .Select(path => new TemplateViewModel
                {
                    Name = new DirectoryInfo(path).Name,
                    ShortName = new DirectoryInfo(path).Name,
                    Language = "C#",
                    Tags = "Web/Microservice",
                })
                .ToList();
        }

        public List<ProjectDependency> GetDependencies(string shortName)
        {
            shortName = string.IsNullOrEmpty(shortName) ? DefaultTemplateName : shortName;
            var list = GetAvailableTemplates();
            var selectedTemplate = list.FirstOrDefault(x => x.ShortName == shortName);

            if (selectedTemplate == null)
            {
                throw new InvalidDataException($"Could not find template with name {shortName} ");
            }

            var templatePath = _templatePath + Path.DirectorySeparatorChar + selectedTemplate.Name;
            var config = _mustacheConfig.GetMustacheConfigSchema(templatePath);

            return config.Params
                .Where(p => p.Description.ToLower().Contains("steeltoe"))
                .Select(p => new ProjectDependency
                {
                    Name = p.FriendlyName ?? GetFriendlyName(p.Name),
                    ShortName = p.Name,
                    Description = p.Description,
                }).ToList();
        }

        public void ClearCache()
        {
            throw new NotImplementedException();
        }

        private byte[] GenerateProjectArchive(GeneratorModel model)
        {
            byte[] archiveBytes;
            var listOfFiles = GenerateProjectFiles(model);

            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var (key, value) in listOfFiles)
                    {
                        _logger.LogDebug(key);
                        var ef = archive.CreateEntry(key, CompressionLevel.Optimal);
                        using (var entryStream = ef.Open())
                        using (var fileToCompress = new MemoryStream(Encoding.UTF8.GetBytes(value)))
                        {
                            fileToCompress.CopyTo(entryStream);
                        }
                    }
                }

                archiveBytes = memoryStream.ToArray();
            }

            return archiveBytes;
        }

        private string Render(string name, string input, object view)
        {
            try
            {
                return _stubble.Render(input, view);
            }
            catch (Exception ex)
            {
                throw new Exception("Error rendering " + name, ex);
            }
        }

        private string GetFriendlyName(string name)
        {
            return FriendlyNames?.ContainsKey(name) == true ? FriendlyNames[name] : name;
        }
    }
}