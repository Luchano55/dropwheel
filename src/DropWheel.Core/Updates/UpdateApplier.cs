namespace DropWheel.Updates;

/// <summary>
/// Раскладывает скачанные файлы поверх программы. Занятый файл (запущенный DropWheel.exe) Windows
/// не даёт перезаписать, но даёт переименовать: старый → «имя.old», новый — на его место.
/// Остатки *.old удаляются при следующем запуске (<see cref="CleanupOld"/>).
/// </summary>
public static class UpdateApplier
{
    public const string OldSuffix = ".old";

    /// <returns>Относительные пути заменённых/добавленных файлов.</returns>
    public static IReadOnlyList<string> Apply(string stagingDir, string targetDir)
    {
        var changed = new List<string>();
        foreach (var source in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingDir, source);
            var target = Path.GetFullPath(Path.Combine(targetDir, relative));
            // Защита: файл из архива не может лечь вне папки программы.
            if (!target.StartsWith(Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Путь вне папки программы: " + relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                var old = target + OldSuffix;
                if (File.Exists(old)) File.Delete(old);
                File.Move(target, old);
            }
            File.Move(source, target);
            changed.Add(relative);
        }
        return changed;
    }

    /// <summary>Удалить *.old после перезапуска. Ошибки (файл ещё занят) — молча, попробуем в следующий раз.</summary>
    public static int CleanupOld(string targetDir)
    {
        var removed = 0;
        foreach (var old in Directory.EnumerateFiles(targetDir, "*" + OldSuffix, SearchOption.AllDirectories))
        {
            try { File.Delete(old); removed++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
