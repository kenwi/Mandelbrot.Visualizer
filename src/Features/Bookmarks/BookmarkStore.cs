using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mandelbrot.Visualizer.Features.Bookmarks;

public sealed record BookmarkFile(int Version, List<BookmarkEntry> Bookmarks);

/// <summary>
/// Persists curated landmarks and user-saved bookmarks together in bookmarks.json.
/// Automatically seeds the JSON file from CuratedBookmarks when it does not yet exist.
/// </summary>
public static class BookmarkStore
{
    public const string DefaultFileName = "bookmarks.json";

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

    public static List<BookmarkEntry> LoadOrSeed(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                BookmarkFile? file = JsonSerializer.Deserialize<BookmarkFile>(json, JsonOptions);
                if (file?.Bookmarks is { Count: > 0 })
                    return file.Bookmarks;
            }
            catch
            {
                // Fall back to curated defaults if the file is unreadable or malformed.
            }
        }

        List<BookmarkEntry> defaults = CuratedBookmarks.CreateDefaults();
        TrySave(path, defaults);
        return defaults;
    }

    public static bool TrySave(string path, IReadOnlyList<BookmarkEntry> bookmarks)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var payload = new BookmarkFile(1, [.. bookmarks]);
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
