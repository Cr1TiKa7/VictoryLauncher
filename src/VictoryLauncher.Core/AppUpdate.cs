using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>Versions of the launcher: three numbers. 0.0.0 is a build nobody released.</summary>
public static partial class ReleaseVersion
{
    private const int LargestPart = 65534;

    [GeneratedRegex(@"\Av?(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z")]
    private static partial Regex Pattern();

    public static Version Local { get; } = new(0, 0, 0);

    public static Version? Parse(string? text)
    {
        var match = Pattern().Match(text ?? "");
        if (!match.Success)
            return null;
        var parts = match.Groups.Values.Skip(1).Select(group => int.Parse(group.Value)).ToArray();
        return parts.All(part => part <= LargestPart) ? new Version(parts[0], parts[1], parts[2]) : null;
    }

    /// <summary>An assembly version has a fourth number, releases do not.</summary>
    public static Version Of(Version assembly) => new(assembly.Major, assembly.Minor, Math.Max(assembly.Build, 0));
}

/// <summary>A newer release and what its zip has to look like.</summary>
public sealed record UpdateInfo(Version Version, Uri Download, string Sha256, long Size);

/// <summary>
/// The launcher's own updates from its GitHub releases. Checksum and file come from the same account: that catches
/// a damaged download, not somebody who took over the account.
/// </summary>
public static partial class AppUpdate
{
    private const string Repository = "Cr1TiKa7/VictoryLauncher";
    private const string LatestRelease = $"https://api.github.com/repos/{Repository}/releases/latest";
    private const string DownloadRoot = $"https://github.com/{Repository}/releases/download/";
    private const string ProgramName = "VictoryLauncher";
    private const string ExeInZip = ProgramName + ".exe";
    private const long LargestZip = 500L * 1024 * 1024;

    private sealed record Release(string? TagName, bool Draft, bool Prerelease, Asset?[]? Assets);

    private sealed record Asset(string? Name, string? BrowserDownloadUrl, string? Digest, long Size);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [GeneratedRegex(@"\Asha256:(?<hash>[0-9a-fA-F]{64})\z")]
    private static partial Regex Digest();

    /// <returns>The update to offer, if any, and a line for the log.</returns>
    public static (UpdateInfo? Update, string Note) ReadLatest(string json, Version current)
    {
        Release? release;
        try
        {
            release = JsonSerializer.Deserialize<Release>(json, Json);
        }
        catch (JsonException)
        {
            return (null, "Update check: the answer from GitHub could not be read.");
        }

        if (current == ReleaseVersion.Local)
            return (null, "Local build, it is not updated.");
        if (release is null || release.Draft || release.Prerelease || ReleaseVersion.Parse(release.TagName) is not { } version)
            return (null, $"Update check: the latest release has no version to compare with ({release?.TagName}).");
        if (version <= current)
            return (null, $"The launcher is up to date ({current}).");

        var name = $"{ProgramName}-v{version}-win-x64.zip";
        var asset = release.Assets?.FirstOrDefault(a => a?.Name == name);
        if (asset is null)
            return (null, $"Version {version} is out, but without {name}.");

        var digest = Digest().Match(asset.Digest ?? "");
        if (!digest.Success)
            return (null, $"Version {version} is out, but without a SHA-256 checksum. It is not offered.");
        if (asset.Size is <= 0 or > LargestZip)
            return (null, $"Version {version} is out, but its size is implausible ({asset.Size} bytes).");
        // Uri resolves dot segments, so the comparison is with the address that would really be asked.
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var url)
            || !url.AbsoluteUri.Equals($"{DownloadRoot}{release.TagName}/{name}", StringComparison.OrdinalIgnoreCase))
            return (null, $"Version {version} is out, but its download does not point to this repository.");

        return (new(version, url, digest.Groups["hash"].Value.ToLowerInvariant(), asset.Size),
            $"Version {version} is out (this is {current}).");
    }

    /// <summary>Asks GitHub for the newest release. Never throws for anything the network can do.</summary>
    public static async Task<(UpdateInfo? Update, string Note)> CheckAsync(HttpClient http, Version current,
        TimeSpan limit, CancellationToken cancel)
    {
        using var within = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        within.CancelAfter(limit);
        try
        {
            using var request = Request(new Uri(LatestRelease));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, within.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, "Update check: there is no release yet.");
            if (!response.IsSuccessStatusCode)
                return (null, $"Update check: GitHub answers with {(int)response.StatusCode}.");
            return ReadLatest(await response.Content.ReadAsStringAsync(within.Token), current);
        }
        catch (OperationCanceledException)
        {
            return (null, "Update check: no answer from GitHub.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return (null, "Update check not possible: " + e.Message);
        }
    }

    /// <summary>Downloads the zip next to the exe, checks it and takes the launcher out of it.</summary>
    /// <returns>The path of the new program, not yet in place.</returns>
    /// <exception cref="InvalidDataException">The download is not what the release announced.</exception>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo update, string exePath,
        Action<long, long>? progress, CancellationToken cancel)
    {
        var id = NewId();
        var zipPath = $"{exePath}.zip-{id}";
        var fresh = $"{exePath}.new-{id}";
        var complete = false;
        try
        {
            using var request = Request(update.Download);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancel))
            await using (var target = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    received += read;
                    if (received > update.Size)
                        throw new InvalidDataException("The download is larger than announced.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                    progress?.Invoke(received, update.Size);
                    cancel.ThrowIfCancellationRequested();
                }
            }

            if (received != update.Size)
                throw new InvalidDataException("The download is incomplete.");
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The checksum of the download does not match.");

            // Only this one entry is written, to a path chosen here: nothing else in the zip can land anywhere.
            using (var zip = ZipFile.OpenRead(zipPath))
                (zip.GetEntry(ExeInZip) ?? throw new InvalidDataException($"{ExeInZip} is missing in the ZIP."))
                    .ExtractToFile(fresh);
            complete = true;
            return fresh;
        }
        finally
        {
            TryDelete(zipPath);
            if (!complete)
                TryDelete(fresh);
        }
    }

    /// <summary>Puts the new program in place. Windows allows renaming a running program, not overwriting it.</summary>
    /// <returns>Where the old program is now.</returns>
    public static string Swap(string newFile, string exePath)
    {
        var old = $"{exePath}.old-{NewId()}";
        File.Move(exePath, old);
        try
        {
            File.Move(newFile, exePath);
        }
        catch
        {
            File.Move(old, exePath);
            throw;
        }
        return old;
    }

    public static void Restore(string oldFile, string exePath)
    {
        if (File.Exists(exePath))
            File.Move(exePath, $"{exePath}.new-{NewId()}");
        File.Move(oldFile, exePath);
    }

    /// <summary>Removes what earlier updates left next to the exe. What is still in use stays for next time.</summary>
    public static void CleanUp(string exePath)
    {
        if (Path.GetDirectoryName(exePath) is not { Length: > 0 } folder)
            return;
        var leftover = new Regex($@"\A{Regex.Escape(Path.GetFileName(exePath))}\.(old|new|zip)-[0-9a-f]{{8}}\z",
            RegexOptions.IgnoreCase);
        try
        {
            foreach (var file in Directory.GetFiles(folder).Where(file => leftover.IsMatch(Path.GetFileName(file))))
                TryDelete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // tidying up must never get in the way
        }
    }

    private const string AfterUpdate = "--after-update";

    /// <summary>What the new version is started with, so that it knows whom to wait for.</summary>
    public static string[] RestartArguments(int processId) => [AfterUpdate, processId.ToString()];

    /// <summary>
    /// Waits for the version that started this one: it holds its own program file until it has ended. Versions up to
    /// 1.0.0 also save the settings only then.
    /// </summary>
    public static void WaitForPredecessor(IReadOnlyList<string> arguments, TimeSpan limit)
    {
        if (arguments is not [AfterUpdate, var text, ..] || !int.TryParse(text, out var id) || id <= 0)
            return;
        try
        {
            using var predecessor = Process.GetProcessById(id);
            predecessor.WaitForExit(limit);
        }
        // already gone, or not a process this one may watch
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }

    private static HttpRequestMessage Request(Uri address)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        // GitHub refuses requests without one
        request.Headers.UserAgent.ParseAdd(ProgramName);
        return request;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
