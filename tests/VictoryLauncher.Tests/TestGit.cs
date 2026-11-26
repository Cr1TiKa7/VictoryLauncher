using System.Diagnostics;

namespace VictoryLauncher.Tests;

/// <summary>Git for tests that build their own throwaway repositories.</summary>
internal static class TestGit
{
    public static string Run(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.name=test", "-c", "user.email=test@example.invalid" }.Concat(args))
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var error = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, error.Result);
        return output.Trim();
    }

    public static void Delete(string root)
    {
        // git marks its object files read only
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
