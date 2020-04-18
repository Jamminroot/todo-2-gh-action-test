using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Newtonsoft.Json;

namespace MetricsCollector
{
    class Program
    {
        private const string TempXmlPath = "MetricsCollector.xml";

        private static RepositoryMetrics ParseMetrics(string repoName, IEnumerator enumerator)
        {
            var metrics = new RepositoryMetrics();
            metrics.Name = repoName;
            enumerator.Reset();
            while (enumerator.MoveNext())
            {
                var item = enumerator.Current as XmlElement;
                var name = item.GetAttribute("Name");
                int value;
                if (int.TryParse(item.GetAttribute("Value"), out value))
                {

                }
                switch (name)
                {
                    case "MaintainabilityIndex":
                        metrics.MaintainabilityIndex = value;
                        break;
                    case "CyclomaticComplexity":
                        metrics.CyclomaticComplexity = value;
                        break;
                    case "ClassCoupling":
                        metrics.ClassCoupling = value;
                        break;
                    case "DepthOfInheritance":
                        metrics.DepthOfInheritance = value;
                        break;
                    case "SourceLines":
                        metrics.SourceLines = value;
                        break;
                    case "ExecutableLines":
                        metrics.ExecutableLines = value;
                        break;
                }
            }
            return metrics;
        }
        
        // TODO Remove default param
        private static string RunShellCommand(string command, string path = "")
        {
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;

            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C cd " + path + " &&    " + command;
            process.StartInfo = startInfo;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            //process.WaitForInputIdle();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "";
        }

        private static void PrepareMetricsXml(string projPath)
        {
            RunShellCommand($"metrics.exe /p:{projPath} /o:{TempXmlPath}");
        }

        private static bool CheckoutRepoOnDate(string repo, string branch, DateTime dateTime)
        {
            var formattedDateTime = dateTime.ToString("yyyy-MM-dd HH:mm");
            var hash = RunShellCommand($"git rev-list -n 1 --before=\"{formattedDateTime}\" {branch}", repo);
            if (hash.Length == 0) { return false; }
            RunShellCommand($"git checkout {hash} --recurse-submodules", repo);
            return true;
        }
        private static void ResetRepo(string repo)
        {
            Console.WriteLine("Resetting repository: {0}", repo);
            RunShellCommand($"git clean -x -d -f", repo);
            RunShellCommand($"git reset --hard", repo);
        }

        private static string GetProjFullFilename(string path)
        {
            var files = Directory.EnumerateFiles(path);
            return files.FirstOrDefault(f => f.EndsWith(".csproj"));
        }

        private static RepositoryMetrics GatherRepoMetrics(TrackedRepo repo)
        {
            if (!Directory.Exists(repo.Path))
            {
                Console.WriteLine("Repo folder {0} not found", repo.Path);
                return null;
            }
            var result = new RepositoryMetrics();
            result.Name = repo.Name;
            var projFile = GetProjFullFilename(repo.Path);
            PrepareMetricsXml(projFile);



            XmlDocument doc = new XmlDocument();
            doc.Load(TempXmlPath);
            var enumerator = doc.GetElementsByTagName("Assembly").GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return null;
            }

            var node = (XmlElement)enumerator.Current;
            return ParseMetrics(repo.Name, node.GetElementsByTagName("Metrics").Item(0).ChildNodes.GetEnumerator());
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Specify file config file.");
                return;
            }

            if (!File.Exists(args[0]))
            {
                Console.WriteLine("File not found");
                return;
            }
            Settings settings;
            try
            {
                settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(args[0]));
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to parse settings.");
                return;
            }
            var result = new Dictionary<DateTime, List<RepositoryMetrics>>();
            var curDate = settings.StartDate;

            Console.WriteLine("Preparing repositories.");
            settings.TrackedRepos.ForEach(item =>
            {
                ResetRepo(item.Path);
            });

            while (curDate <= settings.EndDate)
            {
                Console.WriteLine("Parsing {0}", curDate);
                var list = new List<RepositoryMetrics>();
                RepositoryMetrics sum = new RepositoryMetrics
                {
                    Name = "Total"
                };
                list.Add(sum);

                settings.TrackedRepos.ForEach(item =>
                {
                    if (CheckoutRepoOnDate(item.Path, item.Branch, curDate))
                    {
                        var metrics = GatherRepoMetrics(item);
                        sum.Add(metrics);
                    }                   
                });
                result.Add(curDate, list);
                curDate = curDate.AddDays(1);
            }

            Console.WriteLine("Resetting repositories back to last commit.");
            settings.TrackedRepos.ForEach(item =>
            {
                CheckoutRepoOnDate(item.Path, "master", DateTime.Now);
            });
            Console.WriteLine("------------------------------------------------------------");
            foreach (var pair in result)
            {
                Console.WriteLine(pair.Key);
                pair.Value.ForEach(Console.WriteLine);
            }
            Console.ReadKey();
        }
    }
}
