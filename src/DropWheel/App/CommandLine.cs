namespace DropWheel.App;

/// <summary>
/// Аргументы DropWheel.exe:
///   --convert &lt;путь&gt;...  — из меню проводника (по одному пути на процесс);
///   --register / --unregister — добавить/убрать пункт меню и выйти;
///   --after-update &lt;сборка&gt; — запуск самим собой после обновления.
/// Пути без ключа тоже считаются файлами для конвертации.
/// </summary>
internal sealed record CommandLine(IReadOnlyList<string> ConvertPaths, bool? Register, int? AfterUpdate = null)
{
    public static CommandLine Parse(string[] args)
    {
        var paths = new List<string>();
        bool? register = null;
        int? afterUpdate = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--convert": break;
                case "--register": register = true; break;
                case "--unregister": register = false; break;
                case "--after-update":
                    afterUpdate = i + 1 < args.Length && int.TryParse(args[i + 1], out var build) ? build : 0;
                    i++;
                    break;
                default:
                    // Служебные ключи уведомлений Windows (-ToastActivated и т.п.) — не файлы.
                    if (!arg.StartsWith('-') && File.Exists(arg)) paths.Add(Path.GetFullPath(arg));
                    break;
            }
        }
        return new CommandLine(paths, register, afterUpdate);
    }
}
