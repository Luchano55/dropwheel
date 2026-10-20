namespace DropWheel.App;

/// <summary>
/// Аргументы DropWheel.exe:
///   --convert &lt;путь&gt;...  — из меню проводника (по одному пути на процесс);
///   --register / --unregister — добавить/убрать пункт меню и выйти.
/// Пути без ключа тоже считаются файлами для конвертации.
/// </summary>
internal sealed record CommandLine(IReadOnlyList<string> ConvertPaths, bool? Register)
{
    public static CommandLine Parse(string[] args)
    {
        var paths = new List<string>();
        bool? register = null;
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--convert": break;
                case "--register": register = true; break;
                case "--unregister": register = false; break;
                default:
                    // Служебные ключи уведомлений Windows (-ToastActivated и т.п.) — не файлы.
                    if (!arg.StartsWith('-') && File.Exists(arg)) paths.Add(Path.GetFullPath(arg));
                    break;
            }
        }
        return new CommandLine(paths, register);
    }
}
