using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mandelbrot.Visualizer.Features.Mandelbulb;

public sealed record MandelbulbBookmarkFile(int Version, List<MandelbulbBookmarkEntry> Bookmarks);

/// <summary>
/// Persists curated 3D Mandelbulb landmarks and user-saved 3D spots in
/// mandelbulb-bookmarks.json, auto-seeding defaults on first launch.
/// </summary>
public static class MandelbulbBookmarkStore
{
    public const string DefaultFileName = "mandelbulb-bookmarks.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ResolveDefaultPath()
    {
        string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultFileName);
        if (File.Exists(cwdPath))
            return cwdPath;

        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, DefaultFileName);
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return cwdPath;
    }

    public static List<MandelbulbBookmarkEntry> LoadOrSeed(string path)
    {
        List<MandelbulbBookmarkEntry> defaults = MandelbulbLandmarks.CreateDefaults();
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                MandelbulbBookmarkFile? file = JsonSerializer.Deserialize<MandelbulbBookmarkFile>(json, JsonOptions);
                if (file?.Bookmarks is { Count: > 0 })
                {
                    // Append any newly added curated landmarks without overwriting user-saved spots.
                    var existing = new HashSet<string>(file.Bookmarks.Select(b => b.DisplayName), StringComparer.OrdinalIgnoreCase);
                    bool updated = false;
                    foreach (MandelbulbBookmarkEntry curated in defaults)
                    {
                        if (existing.Add(curated.DisplayName))
                        {
                            file.Bookmarks.Add(curated);
                            updated = true;
                        }
                    }

                    if (updated)
                        TrySave(path, file.Bookmarks);

                    return file.Bookmarks;
                }
            }
            catch
            {
                // Fall back to curated defaults if malformed.
            }
        }

        TrySave(path, defaults);
        return defaults;
    }

    public static bool TrySave(string path, IReadOnlyList<MandelbulbBookmarkEntry> bookmarks)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var payload = new MandelbulbBookmarkFile(1, [.. bookmarks]);
            string json = JsonSerializer.Serialize(payload, JsonOptions);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json + Environment.NewLine);
            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
