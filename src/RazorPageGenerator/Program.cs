// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Razor.Evolution;

namespace RazorPageGenerator
{
    public class Program
    {
        public static int Main(string[] args)
        {
            if (args == null || args.Length != 1)
            {
                Console.WriteLine("Invalid argument(s).");
                Console.WriteLine("Usage:   dotnet razorpagegenerator <root-namespace-of-views>");
                Console.WriteLine("Example: dotnet razorpagegenerator Microsoft.AspNetCore.Diagnostics.RazorViews");
                return 1;
            }

            var rootNamespace = args[0];
            var targetProjectDirectory = Directory.GetCurrentDirectory();
            var results = MainCore(rootNamespace, targetProjectDirectory);

            foreach (var result in results)
            {
                File.WriteAllText(result.FilePath, result.GeneratedCode);
            }

            Console.WriteLine();
            Console.WriteLine($"{results.Count} files successfully generated.");
            Console.WriteLine();
            return 0;
        }

        public static IList<RazorPageGeneratorResult> MainCore(string rootNamespace, string targetProjectDirectory)
        {
            var razorEngine = RazorEngine.Create(builder =>
            {
                builder
                    .SetNamespace(rootNamespace)
                    .SetBaseType("Microsoft.Extensions.RazorViews.BaseView")
                    .ConfigureClass((document, @class) =>
                    {
                        @class.Name = Path.GetFileNameWithoutExtension(document.Source.FileName);
                        @class.AccessModifier = "internal";
                    });

                builder.Features.Add(new RemovePragmaChecksumFeature());

            });

            var viewDirectories = Directory.EnumerateDirectories(targetProjectDirectory, "Views", SearchOption.AllDirectories);
            var razorProject = new FileSystemRazorProject(targetProjectDirectory);
            var templateEngine = new RazorTemplateEngine(razorEngine, razorProject);

            var fileCount = 0;

            var results = new List<RazorPageGeneratorResult>();
            foreach (var viewDir in viewDirectories)
            {
                Console.WriteLine();
                Console.WriteLine("  Generating code files for views in {0}", viewDir);
                var viewDirPath = viewDir.Substring(targetProjectDirectory.Length).Replace('\\', '/');
                var cshtmlFiles = razorProject.EnumerateItems(viewDirPath).Cast<FileSystemRazorProjectItem>();

                if (!cshtmlFiles.Any())
                {
                    Console.WriteLine("  No .cshtml files were found.");
                    continue;
                }

                foreach (var item in cshtmlFiles)
                {
                    Console.WriteLine("    Generating code file for view {0}...", item.FileName);
                    results.Add(GenerateCodeFile(templateEngine, item));
                    Console.WriteLine("      Done!");
                    fileCount++;
                }
            }

            return results;
        }

        private static RazorPageGeneratorResult GenerateCodeFile(RazorTemplateEngine templateEngine, FileSystemRazorProjectItem projectItem)
        {
            var projectItemWrapper = new FileSystemRazorProjectItemWrapper(projectItem);
            var cSharpDocument = templateEngine.GenerateCode(projectItemWrapper);
            if (cSharpDocument.Diagnostics.Any())
            {
                var diagnostics = string.Join(Environment.NewLine, cSharpDocument.Diagnostics);
                Console.WriteLine($"One or more parse errors encountered. This will not prevent the generator from continuing: {Environment.NewLine}{diagnostics}.");
            }

            var generatedCodeFilePath = Path.ChangeExtension(projectItem.PhysicalPath, ".Designer.cs");
            return new RazorPageGeneratorResult
            {
                FilePath = generatedCodeFilePath,
                GeneratedCode = cSharpDocument.GeneratedCode,
            };
        }

        private class FileSystemRazorProjectItemWrapper : RazorProjectItem
        {
            private readonly FileSystemRazorProjectItem _source;

            public FileSystemRazorProjectItemWrapper(FileSystemRazorProjectItem item)
            {
                _source = item;
            }

            public override string BasePath => _source.BasePath;

            public override string Path => _source.Path;

            // Mask the full name since we don't want a developer's local file paths to be commited.
            public override string PhysicalPath => _source.FileName;

            public override bool Exists => _source.Exists;

            public override Stream Read()
            {
                var processedContent = ProcessFileIncludes();
                return new MemoryStream(Encoding.UTF8.GetBytes(processedContent));
            }

            private string ProcessFileIncludes()
            {
                var basePath = _source.File.DirectoryName;
                var cshtmlContent = File.ReadAllText(_source.PhysicalPath);

                var startMatch = "<%$ include: ";
                var endMatch = " %>";
                var startIndex = 0;
                while (startIndex < cshtmlContent.Length)
                {
                    startIndex = cshtmlContent.IndexOf(startMatch, startIndex);
                    if (startIndex == -1)
                    {
                        break;
                    }
                    var endIndex = cshtmlContent.IndexOf(endMatch, startIndex);
                    if (endIndex == -1)
                    {
                        throw new InvalidOperationException($"Invalid include file format in {_source.PhysicalPath}. Usage example: <%$ include: ErrorPage.js %>");
                    }
                    var includeFileName = cshtmlContent.Substring(startIndex + startMatch.Length, endIndex - (startIndex + startMatch.Length));
                    Console.WriteLine("      Inlining file {0}", includeFileName);
                    var includeFileContent = File.ReadAllText(System.IO.Path.Combine(basePath, includeFileName));
                    cshtmlContent = cshtmlContent.Substring(0, startIndex) + includeFileContent + cshtmlContent.Substring(endIndex + endMatch.Length);
                    startIndex = startIndex + includeFileContent.Length;
                }
                return cshtmlContent;
            }
        }
    }
}
