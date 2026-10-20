namespace DropWheel.Jobs;

/// <summary>Имя результата: photo.heic → photo.jpg; занято → photo (1).jpg, photo (2).jpg…</summary>
public static class OutputNamer
{
    /// <summary>Желаемый путь результата (без проверки занятости).</summary>
    public static string DesiredPath(string input, string outputExtension, string suffix = "", string? outputFolder = null)
    {
        var dir = outputFolder ?? Path.GetDirectoryName(Path.GetFullPath(input))!;
        var name = Path.GetFileNameWithoutExtension(input) + suffix + "." + outputExtension.TrimStart('.');
        return Path.Combine(dir, name);
    }

    /// <summary>Первый свободный вариант пути. Занятым считается и сам исходник.</summary>
    public static string FirstFree(string desired, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        if (!exists(desired)) return desired;

        var dir = Path.GetDirectoryName(desired)!;
        var stem = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!exists(candidate)) return candidate;
        }
    }
}
