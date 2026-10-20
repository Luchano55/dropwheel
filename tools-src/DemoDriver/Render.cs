using System.Globalization;
using System.Text;

namespace DemoDriver;

/// <summary>
/// Монтаж: из сырой записи (raw.mkv) и журнала сценария (timeline.json) — ролик 20–30 с.
/// Вырезает паузы между сценами, ускоряет ожидание конвертации, внизу — полоса с подписями
/// (английская и русская версии), плавное появление и затухание. Результат: demo.mp4 (H.264, faststart, ≤ 10 МБ),
/// demo.gif (≤ 800 px, 15 fps, ≤ 8 МБ), demo-ru.mp4, demo-ru.gif, demo-poster.png.
/// </summary>
internal static class Render
{
    public sealed record Options(string Raw, string TimelinePath, string Ffmpeg, string Font, string FontBold,
        string OutDir, string WorkDir);

    public sealed record Segment(double Start, double End, double Speed)
    {
        public double OutLength => (End - Start) / Speed;
    }

    public sealed record Caption(double Start, double End, string En, string Ru);

    private const double MinTotal = 20;
    private const double MaxTotal = 30;
    private const long MaxMp4Bytes = 10L * 1024 * 1024;
    private const long MaxGifBytes = 8L * 1024 * 1024;
    private const int Fps = 30;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(Options o)
    {
        Directory.CreateDirectory(o.OutDir);
        Directory.CreateDirectory(o.WorkDir);
        if (!File.Exists(o.Raw))
        {
            Log.Error($"нет сырой записи {o.Raw} — монтировать нечего");
            return 1;
        }
        var timeline = Timeline.Load(o.TimelinePath);
        var rawLength = timeline.RecordedSeconds;
        if (rawLength <= 0)
        {
            Log.Error("в журнале сценария нет длины записи");
            return 1;
        }

        var (segments, captions) = Plan(timeline, rawLength);
        var total = segments.Sum(s => s.OutLength);
        Log.Write($"План монтажа: {segments.Count} кусков, итог {total:F1} с (запись {rawLength:F1} с)");
        foreach (var s in segments)
            Log.Write($"  {s.Start,7:F2}–{s.End,7:F2} с  ×{s.Speed:F2}  → {s.OutLength:F2} с");
        foreach (var c in captions)
            Log.Write($"  подпись {c.Start,6:F2}–{c.End,6:F2}: «{c.En}» / «{c.Ru}»");

        // Шрифты и тексты — в рабочую папку: в фильтре ffmpeg тогда короткие пути без «C:» и экранирования.
        File.Copy(o.Font, Path.Combine(o.WorkDir, "font.ttf"), overwrite: true);
        File.Copy(o.FontBold, Path.Combine(o.WorkDir, "font-bold.ttf"), overwrite: true);
        var raw = Path.GetFullPath(o.Raw);
        var logFile = Path.Combine(o.WorkDir, "render-ffmpeg.log");

        var ok = true;
        foreach (var (lang, name) in new[] { ("en", "demo"), ("ru", "demo-ru") })
        {
            for (var i = 0; i < captions.Count; i++)
                File.WriteAllText(Path.Combine(o.WorkDir, $"cap-{lang}-{i}.txt"),
                    lang == "en" ? captions[i].En : captions[i].Ru, new UTF8Encoding(false));

            var mp4 = Path.Combine(o.OutDir, name + ".mp4");
            ok &= EncodeMp4(o, raw, timeline, segments, captions, lang, mp4, total, logFile);
            if (!File.Exists(mp4)) continue;
            ok &= EncodeGif(o, mp4, Path.Combine(o.OutDir, name + ".gif"), logFile);
            if (lang == "en")
            {
                // Кадр для README: колесо с подсвеченным сектором.
                var posterAt = Map(segments, (timeline.At("photo-drop") ?? timeline.At("photo-wheel") ?? 0) - 0.3);
                Proc.Run(o.Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-ss", F(Math.Max(0, posterAt)), "-i", mp4, "-frames:v", "1",
                    Path.Combine(o.OutDir, "demo-poster.png")], o.WorkDir, logFile, TimeSpan.FromMinutes(1));
            }
        }
        return ok ? 0 : 1;
    }

    /// <summary>Куски записи и подписи (во времени готового ролика).</summary>
    public static (List<Segment> Segments, List<Caption> Captions) Plan(Timeline t, double rawLength)
    {
        var segs = new List<Segment>();
        void Add(double? start, double? end, double maxOut = double.MaxValue)
        {
            if (start is not { } s || end is not { } e) return;
            s = Math.Max(s, segs.Count > 0 ? segs[^1].End : 0);
            e = Math.Min(e, rawLength);
            if (e - s < 0.05) return;
            segs.Add(new Segment(s, e, Math.Max(1, (e - s) / maxOut)));
        }
        double? Prev() => segs.Count > 0 ? segs[^1].End : null;
        double? Plus(double? v, double d) => v + d;

        var raw = new List<(double? Start, double? End, string En, string Ru)>();
        if (t.At("photo-press") is not { } photoPress)
        {
            // Сценарий не дошёл даже до первого перетаскивания — показываем запись целиком (для разбора).
            Add(0, rawLength, MaxTotal - 2);
            raw.Add((0, rawLength, "Demo scenario did not complete — see logs", "Сценарий не прошёл — смотрите журналы"));
        }
        else
        {
            var photoDrop = t.At("photo-drop") ?? t.At("photo-wheel") ?? photoPress + 6;
            var photoDone = t.At("photo-done");
            Add(photoPress - 2.0, photoDrop + 0.4);                  // подход к файлу, перетаскивание, колесо, бросок
            if (photoDone is { } pd)
            {
                Add(Prev(), pd, maxOut: 1.0);                         // ожидание конвертации — быстро
                Add(pd, pd + 1.6);                                    // новый файл рядом
            }

            var videoPress = t.At("video-press");
            var videoDrop = t.At("video-drop");
            var jobs = t.At("jobs-shown") ?? t.At("jobs-launch");
            var videoDone = t.At("video-done");
            var end = t.At("end") ?? rawLength;
            if (videoPress is { } vp)
            {
                Add(vp - 0.7, (videoDrop ?? t.At("video-wheel") ?? vp + 6) + 0.4);
                if (jobs is { } j) Add(Prev(), j + 0.8, maxOut: 2.0);   // открывается окно задач
                if (videoDone is { } vd)
                {
                    Add(Prev(), vd, maxOut: 3.0);                       // прогресс сжатия — ускоренно
                    Add(vd, end, maxOut: 3.5);                          // итог: оба файла рядом с исходниками
                }
                else
                {
                    Add(Prev(), end, maxOut: 4.0);
                }
            }
            else
            {
                Add(Prev(), end, maxOut: 3.0);
            }

            var photoSpace = t.At("photo-space");
            var photoWheel = t.At("photo-wheel");
            raw.Add((segs[0].Start, photoSpace, "Drag a file from Explorer…", "Тащим файл из проводника…"));
            raw.Add((photoSpace, Plus(photoWheel, 0.9), "…press Space — the wheel appears", "…жмём Пробел — появляется колесо"));
            raw.Add((Plus(photoWheel, 0.9), photoDrop + 0.4, "Drop it on a format", "Бросаем на нужный формат"));
            raw.Add((photoDrop + 0.4, Plus(photoDone, 1.6), "Done — the PNG is next to the original", "Готово — PNG рядом с исходником"));

            var videoSpace = t.At("video-space");
            var videoToggle = t.At("video-toggle");
            raw.Add((videoPress - 0.7, videoSpace, "Video works the same way", "С видео — так же"));
            raw.Add((videoSpace, videoToggle, "Space — formats", "Пробел — форматы"));
            raw.Add((videoToggle, Plus(videoDrop, 0.4), "Space again — tools: Compress", "Ещё раз Пробел — инструменты: «Сжать»"));
            raw.Add((Plus(videoDrop, 0.4), videoDone, "Progress — in the jobs window", "Прогресс — в окне задач"));
            raw.Add((videoDone, end, "Done — all offline, nothing uploaded", "Готово — всё офлайн, без интернета"));
        }

        // Короче 20 с — меньше ускоряем ожидание (с конца), пока не наберётся 20 с.
        var total = segs.Sum(s => s.OutLength);
        for (var i = segs.Count - 1; i >= 0 && total < MinTotal; i--)
        {
            var s = segs[i];
            if (s.Speed <= 1) continue;
            var wanted = Math.Min(s.End - s.Start, s.OutLength + (MinTotal - total));
            segs[i] = s with { Speed = Math.Max(1, (s.End - s.Start) / wanted) };
            total = segs.Sum(x => x.OutLength);
        }

        // Длиннее 30 с — равномерно ускоряем всё.
        if (total > MaxTotal - 0.5)
        {
            var k = total / (MaxTotal - 0.5);
            segs = segs.Select(s => s with { Speed = s.Speed * k }).ToList();
        }

        var captions = new List<Caption>();
        foreach (var (start, end, en, ru) in raw)
        {
            if (start is not { } s || end is not { } e) continue;
            var a = Map(segs, s);
            var b = Map(segs, e);
            if (b - a < 0.6) continue;
            captions.Add(new Caption(a, b, en, ru));
        }
        return (segs, captions);
    }

    /// <summary>Секунда записи → секунда готового ролика (паузы между кусками схлопываются).</summary>
    public static double Map(IReadOnlyList<Segment> segs, double raw)
    {
        var acc = 0.0;
        foreach (var s in segs)
        {
            if (raw <= s.Start) return acc;
            if (raw <= s.End) return acc + (raw - s.Start) / s.Speed;
            acc += s.OutLength;
        }
        return acc;
    }

    private static bool EncodeMp4(Options o, string raw, Timeline t, List<Segment> segs, List<Caption> caps,
        string lang, string output, double total, string logFile)
    {
        var graph = BuildGraph(t, segs, caps, lang, total);
        File.WriteAllText(Path.Combine(o.WorkDir, $"graph-{lang}.txt"), graph, new UTF8Encoding(false));
        foreach (var crf in new[] { 23, 27, 31, 35 })
        {
            var code = Proc.Run(o.Ffmpeg,
            [
                "-hide_banner", "-nostdin", "-y", "-i", raw, "-filter_complex", graph, "-map", "[out]", "-an",
                "-c:v", "libx264", "-preset", "slow", "-crf", crf.ToString(Inv), "-pix_fmt", "yuv420p",
                "-r", Fps.ToString(Inv), "-movflags", "+faststart", output,
            ], o.WorkDir, logFile, TimeSpan.FromMinutes(10));
            if (code != 0 || !File.Exists(output))
            {
                Log.Error($"ffmpeg не собрал {Path.GetFileName(output)} (код {code}) — фильтр в graph-{lang}.txt");
                return false;
            }
            var size = new FileInfo(output).Length;
            Log.Write($"{Path.GetFileName(output)}: {size / 1048576.0:F2} МБ (CRF {crf})");
            if (size <= MaxMp4Bytes) return true;
        }
        Log.Error($"{Path.GetFileName(output)} больше 10 МБ даже с CRF 35");
        return false;
    }

    private static bool EncodeGif(Options o, string mp4, string gif, string logFile)
    {
        foreach (var (width, fps, colors) in new[] { (800, 15, 128), (720, 12, 96), (640, 10, 64) })
        {
            var graph = $"fps={fps},scale={width}:-1:flags=lanczos,split[a][b];"
                + $"[a]palettegen=max_colors={colors}:stats_mode=diff[p];"
                + "[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle";
            var code = Proc.Run(o.Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-i", mp4, "-filter_complex", graph, "-loop", "0", gif],
                o.WorkDir, logFile, TimeSpan.FromMinutes(10));
            if (code != 0 || !File.Exists(gif))
            {
                Log.Error($"ffmpeg не собрал {Path.GetFileName(gif)} (код {code})");
                return false;
            }
            var size = new FileInfo(gif).Length;
            Log.Write($"{Path.GetFileName(gif)}: {size / 1048576.0:F2} МБ ({width} px, {fps} fps, {colors} цветов)");
            if (size <= MaxGifBytes) return true;
        }
        Log.Error($"{Path.GetFileName(gif)} больше 8 МБ даже в 640 px");
        return false;
    }

    /// <summary>Граф фильтров: куски → склейка → 30 fps → полоса снизу → подписи → затухание.</summary>
    public static string BuildGraph(Timeline t, IReadOnlyList<Segment> segs, IReadOnlyList<Caption> caps, string lang, double total)
    {
        var width = t.ScreenWidth > 0 ? t.ScreenWidth : 1280;
        var height = t.ScreenHeight > 0 ? t.ScreenHeight : 720;
        var scale = width > 1920 ? 1920.0 / width : 1.0;
        var outH = (int)(height * scale) / 2 * 2;
        var band = Math.Max(64, (int)Math.Round(outH * 0.11 / 2) * 2);
        var fontSize = (int)Math.Round(band * 0.40);
        var brandSize = (int)Math.Round(band * 0.30);
        var textY = $"h-{band}+({band}-{fontSize})/2";
        var brandY = $"h-{band}+({band}-{brandSize})/2";

        var sb = new StringBuilder();
        sb.Append("[0:v]setpts=PTS-STARTPTS,split=").Append(segs.Count);
        for (var i = 0; i < segs.Count; i++) sb.Append("[r").Append(i).Append(']');
        sb.Append(';');
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            sb.Append($"[r{i}]trim=start={F(s.Start)}:end={F(s.End)},setpts=(PTS-STARTPTS)/{F(s.Speed)}[s{i}];");
        }
        for (var i = 0; i < segs.Count; i++) sb.Append("[s").Append(i).Append(']');
        sb.Append($"concat=n={segs.Count}:v=1:a=0,fps={Fps}");
        if (scale < 1) sb.Append(",scale=1920:-2:flags=lanczos");
        sb.Append($",pad=iw:ih+{band}:0:0:color=0x16171D");
        sb.Append($",drawtext=fontfile=font-bold.ttf:text=DropWheel:fontsize={brandSize}:fontcolor=0x8AB4F8:x={band / 3}:y={brandY}");
        for (var i = 0; i < caps.Count; i++)
        {
            var c = caps[i];
            var a = c.Start;
            var b = c.End;
            const double fade = 0.25;
            sb.Append($",drawtext=fontfile=font.ttf:textfile=cap-{lang}-{i}.txt:expansion=none:fontsize={fontSize}:fontcolor=white")
              .Append($":x=(w-tw)/2:y={textY}")
              .Append($":alpha='if(lt(t,{F(a + fade)}),(t-{F(a)})/{F(fade)},if(gt(t,{F(b - fade)}),({F(b)}-t)/{F(fade)},1))'")
              .Append($":enable='between(t,{F(a)},{F(b)})'");
        }
        var fadeOut = Math.Max(0, total - 0.7);
        sb.Append($",fade=t=in:st=0:d=0.5,fade=t=out:st={F(fadeOut)}:d=0.7,format=yuv420p[out]");
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.###", Inv);
}
