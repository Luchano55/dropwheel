using System.Globalization;
using System.Text;

namespace DemoDriver;

/// <summary>
/// DemoDriver — робот для демо-ролика DropWheel (см. docs/demo.md и build/demo/record-demo.ps1).
/// Команды:
///   info                                   — диагностика сеанса, экрана, окон
///   set-resolution 1280x720 [WxH …]        — сменить разрешение (первое получившееся)
///   prepare-desktop --wallpaper файл.bmp   — свернуть окна, обои, скрыть значки, показать расширения, курсор
///   open-explorer --folder папка           — открыть папку, поставить окно, крупные значки
///   screenshot файл.png                    — снимок экрана
///   run --folder … --app … --tools … --app-log-dir … --shots … --timeline … [--ffmpeg … --raw … --record-log …]
///       [--photo Отпуск.heic --photo-op to-png --video Видео.mov --video-op video-compress --ring-radius 92]
///                                          — запись экрана + сценарий (коды: 0 ок, 2 тест не прошёл, 3 нет окружения)
///   render --raw … --timeline … --ffmpeg … --font … --font-bold … --out … --work …
///                                          — монтаж demo.mp4 / demo.gif (+ -ru)
/// Общий ключ: --log файл — дописывать журнал ещё и в файл.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Кириллица в журнале CI. Без консоли SetConsoleOutputCP может упасть — тогда как есть.
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length == 0)
        {
            Console.WriteLine("Использование: DemoDriver <info|set-resolution|prepare-desktop|open-explorer|screenshot|run|render> …");
            return 1;
        }
        var command = args[0];
        var (named, positional) = Parse(args.Skip(1));
        if (named.TryGetValue("log", out var logFile)) Log.To(logFile);
        Log.Write($"DemoDriver {command} {string.Join(' ', args.Skip(1))}");

        try
        {
            switch (command)
            {
                case "info":
                    Desktop.PrintInfo();
                    return 0;

                case "set-resolution":
                {
                    var list = positional.Select(ParseSize).OfType<(int, int)>().ToList();
                    if (list.Count == 0) list.Add((1280, 720));
                    return Desktop.SetResolution(list) ? 0 : 1;
                }

                case "prepare-desktop":
                    Desktop.Prepare(Get(named, "wallpaper", Path.Combine(Path.GetTempPath(), "dropwheel-demo-wallpaper.bmp")));
                    return 0;

                case "open-explorer":
                {
                    var wa = Desktop.WorkArea;
                    var rect = new Native.RECT
                    {
                        Left = wa.Left + (int)(wa.Width * 0.03),
                        Top = wa.Top + (int)(wa.Height * 0.06),
                    };
                    rect.Right = rect.Left + (int)(wa.Width * 0.55);
                    rect.Bottom = rect.Top + (int)(wa.Height * 0.80);
                    return Explorer.Open(Require(named, "folder"), rect) == IntPtr.Zero ? 3 : 0;
                }

                case "screenshot":
                    Desktop.Screenshot(positional.FirstOrDefault() ?? "screenshot.png");
                    return 0;

                case "run":
                {
                    var options = new ScenarioOptions(
                        Folder: Path.GetFullPath(Require(named, "folder")),
                        AppExe: Path.GetFullPath(Require(named, "app")),
                        ToolsDir: Path.GetFullPath(Require(named, "tools")),
                        AppLogDir: Path.GetFullPath(Require(named, "app-log-dir")),
                        ShotsDir: Path.GetFullPath(Require(named, "shots")),
                        TimelinePath: Path.GetFullPath(Require(named, "timeline")),
                        Ffmpeg: named.TryGetValue("ffmpeg", out var ff) ? Path.GetFullPath(ff) : null,
                        RawOutput: Path.GetFullPath(Get(named, "raw", "raw.mkv")),
                        RecordLog: Path.GetFullPath(Get(named, "record-log", "record-ffmpeg.log")),
                        PhotoFile: Get(named, "photo", "Отпуск.heic"),
                        PhotoOp: Get(named, "photo-op", "to-png"),
                        VideoFile: Get(named, "video", "Видео.mov"),
                        VideoOp: Get(named, "video-op", "video-compress"),
                        RingRadiusDip: double.Parse(Get(named, "ring-radius",
                            WheelLayout.DefaultRingRadiusDip.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture));
                    return new Scenario(options).Run();
                }

                case "render":
                    return Render.Run(new Render.Options(
                        Raw: Require(named, "raw"),
                        TimelinePath: Require(named, "timeline"),
                        Ffmpeg: Require(named, "ffmpeg"),
                        Font: Require(named, "font"),
                        FontBold: Get(named, "font-bold", Require(named, "font")),
                        OutDir: Require(named, "out"),
                        WorkDir: Require(named, "work")));

                default:
                    Log.Error($"неизвестная команда {command}");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());
            return 1;
        }
    }

    private static (Dictionary<string, string> Named, List<string> Positional) Parse(IEnumerable<string> args)
    {
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();
        using var e = args.GetEnumerator();
        while (e.MoveNext())
        {
            var a = e.Current;
            if (a.StartsWith("--", StringComparison.Ordinal))
                named[a[2..]] = e.MoveNext() ? e.Current : "";
            else
                positional.Add(a);
        }
        return (named, positional);
    }

    private static string Require(Dictionary<string, string> named, string key) =>
        named.TryGetValue(key, out var v) && v.Length > 0 ? v : throw new ArgumentException($"нужен ключ --{key}");

    private static string Get(Dictionary<string, string> named, string key, string fallback) =>
        named.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    private static (int, int)? ParseSize(string s)
    {
        var parts = s.ToLowerInvariant().Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) ? (w, h) : null;
    }
}
