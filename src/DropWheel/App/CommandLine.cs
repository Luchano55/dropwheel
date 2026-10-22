namespace DropWheel.App;

/// <summary>
/// Аргументы DropWheel.exe:
///   --convert &lt;путь&gt;...  — из меню проводника (по одному пути на процесс);
///   --register / --unregister — добавить/убрать пункт меню и выйти;
///   --after-update &lt;сборка&gt; — запуск самим собой после обновления;
///   --autostart — запуск при входе в Windows (без подсказки в трее);
///   --exit — попросить запущенный экземпляр закрыться (установщик перед обновлением и удалением) и дождаться этого.
/// Пути без ключа тоже считаются файлами для конвертации.
/// </summary>
internal sealed record CommandLine(IReadOnlyList<string> ConvertPaths, bool? Register, int? AfterUpdate = null, bool AutoStart = false,
    bool Exit = false)
{
    public const string ExitArgument = "--exit";

    public static CommandLine Parse(string[] args)
    {
        var paths = new List<string>();
        bool? register = null;
        int? afterUpdate = null;
        var autoStart = false;
        var exit = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--convert": break;
                case "--register": register = true; break;
                case "--unregister": register = false; break;
                case Shell.AutoStartRegistration.Argument: autoStart = true; break;
                case ExitArgument: exit = true; break;
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
        return new CommandLine(exit ? [] : paths, register, afterUpdate, autoStart, exit);
    }
}
