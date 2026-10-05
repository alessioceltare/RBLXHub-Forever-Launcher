using System.Diagnostics;
using System.Net;
using System.Text;

namespace RBLXHubLauncher;

internal static class HostsFileInstaller
{
    internal static int CountValidEntries(string sourcePath)
    {
        return ReadEntries(sourcePath).Sum(entry => entry.Hostnames.Count);
    }

    internal static string InstallWithElevation(string sourcePath)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the launcher executable.");
        string resultPath = Path.Combine(
            Path.GetTempPath(),
            $"rblxhub-hosts-{Guid.NewGuid():N}.txt");

        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--apply-hosts");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add(resultPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the elevated hosts-file update.");
            process.WaitForExit();

            string result = File.Exists(resultPath)
                ? File.ReadAllText(resultPath)
                : string.Empty;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result)
                        ? "The elevated hosts-file update failed."
                        : result);
            }

            return result;
        }
        finally
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
        }
    }

    internal static int RunPrivilegedWorker(string sourcePath, string resultPath)
    {
        try
        {
            string result = ApplyEntries(sourcePath);
            File.WriteAllText(resultPath, result, Encoding.UTF8);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException)
        {
            try
            {
                File.WriteAllText(resultPath, exception.Message, Encoding.UTF8);
            }
            catch (IOException)
            {
                return 2;
            }

            return 1;
        }
    }

    private static string ApplyEntries(string sourcePath)
    {
        List<HostEntry> requestedEntries = ReadEntries(sourcePath);
        string hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers",
            "etc",
            "hosts");
        if (!File.Exists(hostsPath))
        {
            throw new FileNotFoundException("The Windows hosts file was not found.", hostsPath);
        }

        string originalContents = File.ReadAllText(hostsPath);
        var existingMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in originalContents.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseEntry(line, out HostEntry entry))
            {
                foreach (string hostname in entry.Hostnames)
                {
                    existingMappings.Add(MappingKey(entry.Address, hostname));
                }
            }
        }

        var additions = new List<string>();
        int addedMappings = 0;
        foreach (HostEntry entry in requestedEntries)
        {
            string[] missingHostnames = entry.Hostnames
                .Where(hostname => existingMappings.Add(MappingKey(entry.Address, hostname)))
                .ToArray();
            if (missingHostnames.Length == 0)
            {
                continue;
            }

            additions.Add($"{entry.Address}\t{string.Join(' ', missingHostnames)}");
            addedMappings += missingHostnames.Length;
        }

        if (additions.Count == 0)
        {
            return "All requested host entries were already present.";
        }

        string backupPath = hostsPath + $".rblxhub-{DateTime.Now:yyyyMMddHHmmssfff}.bak";
        string temporaryPath = hostsPath + $".rblxhub-{Guid.NewGuid():N}.tmp";
        File.Copy(hostsPath, backupPath);

        string updatedContents = originalContents;
        if (updatedContents.Length > 0 &&
            !updatedContents.EndsWith('\n') &&
            !updatedContents.EndsWith('\r'))
        {
            updatedContents += Environment.NewLine;
        }
        updatedContents += string.Join(Environment.NewLine, additions) + Environment.NewLine;

        try
        {
            File.WriteAllText(temporaryPath, updatedContents, new UTF8Encoding(false));
            File.Replace(temporaryPath, hostsPath, null);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return $"Added {addedMappings} host mapping(s). Backup: {backupPath}";
    }

    private static List<HostEntry> ReadEntries(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected hosts entry file was not found.", sourcePath);
        }

        var entries = new List<HostEntry>();
        foreach (string line in File.ReadAllLines(sourcePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (!TryParseEntry(line, out HostEntry entry))
            {
                throw new InvalidDataException($"Invalid hosts entry: {line}");
            }

            entries.Add(entry);
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("The selected hosts entry file contains no usable entries.");
        }

        return entries;
    }

    private static bool TryParseEntry(string line, out HostEntry entry)
    {
        entry = null!;
        string content = line.Split('#', 2)[0].Trim();
        if (content.Length == 0)
        {
            return false;
        }

        string[] tokens = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 ||
            !IPAddress.TryParse(tokens[0], out IPAddress? address) ||
            tokens.Skip(1).Any(hostname => !IsValidHostname(hostname)))
        {
            return false;
        }

        entry = new HostEntry(address.ToString(), tokens.Skip(1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        return true;
    }

    private static bool IsValidHostname(string hostname)
    {
        if (hostname.Length is 0 or > 253)
        {
            return false;
        }

        string normalized = hostname.EndsWith('.') ? hostname[..^1] : hostname;
        return normalized.Split('.').All(label =>
            label.Length is > 0 and <= 63 &&
            label[0] != '-' &&
            label[^1] != '-' &&
            label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }

    private static string MappingKey(string address, string hostname)
    {
        return $"{address}\0{hostname}";
    }

    private sealed record HostEntry(string Address, IReadOnlyList<string> Hostnames);
}
