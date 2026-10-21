using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command-line entry point for the browser build, so the demo can be rebuilt without opening the Editor:
/// <c>Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod WebBuild.Build -buildOutput &lt;dir&gt;</c>
/// Builds every scene enabled in Build Settings, using the Web publishing settings saved in Player Settings.
/// </summary>
public static class WebBuild
{
    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-buildOutput");
        string output = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Builds/Web";

        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"WebBuild: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, " +
                  $"{report.summary.totalErrors} errors, to {output}");

        if (Application.isBatchMode)
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
