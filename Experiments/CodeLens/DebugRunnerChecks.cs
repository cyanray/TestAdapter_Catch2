using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Catch2Interface;

internal static class DebugRunnerChecks
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Provide the full path to the built Catch2CodeLensTests.exe.");
            return 2;
        }
        var source = Path.GetFullPath(args[0]);
        var marker = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "../..", "fail.flag"));
        if (File.Exists(marker)) throw new InvalidOperationException("Rename fail.flag before running checks.");
        var xml = new XmlDocument();
        xml.LoadXml("<Catch2Adapter><FilenameFilter>^Catch2CodeLensTests$</FilenameFilter><DebugBreak>false</DebugBreak></Catch2Adapter>");
        var settings = SettingsManager.Extract(xml.DocumentElement);
        var executor = new Executor(settings, "", "");
        var launches = 0;
        Func<string, string, string, IDictionary<string, string>, int> launch = (file, directory, arguments, environment) =>
        {
            launches++;
            var start = new ProcessStartInfo(file, arguments)
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var pair in environment) start.EnvironmentVariables[pair.Key] = pair.Value;
            using (var process = Process.Start(start)) return process.Id;
        };
        // Substitute process launch for VS attachment while exercising the same
        // wait, report parsing, selection, and temporary-file cleanup code.
        Check(executor.RunDebug("CodeLens passes", source, launch).Outcome == TestOutcomes.Passed, "single pass");
        var group = new TestCaseGroup { Source = source };
        group.Names.Add("CodeLens toggles failure");
        var result = executor.RunDebug(group, launch);
        Check(result.TestResults.Count == 1 && result.TestResults[0].Outcome == TestOutcomes.Passed, "combined pass and exact selection");
        File.WriteAllText(marker, "Intentional test failure");
        try
        {
            Check(executor.RunDebug("CodeLens toggles failure", source, launch).Outcome == TestOutcomes.Failed, "single failure");
            result = executor.RunDebug(group, launch);
            Check(result.TestResults.Count == 1 && result.TestResults[0].Outcome == TestOutcomes.Failed, "combined failure");
        }
        finally { File.Delete(marker); }
        Check(launches == 4, "one process per requested run, no implicit reruns");
        executor.Cancel();
        Check(executor.RunDebug("CodeLens passes", source, launch).Outcome == TestOutcomes.Cancelled && launches == 4,
            "pre-cancelled run does not launch");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(source), Path.GetFileName(source) + ".report.*.xml").Any(), "report cleanup");
        Console.WriteLine("All 7 debug-result checks passed. IDE breakpoint validation is separate.");
        return 0;
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + label);
        Console.WriteLine("PASS: " + label);
    }
}
