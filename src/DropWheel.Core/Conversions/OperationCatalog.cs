using static DropWheel.Conversions.EngineKind;
using static DropWheel.Conversions.MediaKind;


namespace DropWheel.Conversions;

/// <summary>
/// Каталог операций. Новая операция — одна строка.
/// Плейсхолдеры: {jpgq} {webpq} {crf} {crfsmall} {mp3k} — из ConversionSettings; {page} — «стр» / «page»,
/// {frame} — «кадр» / «frame» (язык интерфейса);
/// {acodec} — кодек звука под расширение результата (см. ArgumentBuilder.AudioCodec);
/// {имя} из Operation.Parameters — значение для одной задачи (обрезка видео: {start} {end}, картинки: {x} {y} {w} {h};
/// см. Operation.WithValues).
/// Для qpdf / pdftoppm шаблон берётся целиком: {in} — первый файл, {ins} — все файлы, {out} — результат
/// (файл или временная папка). У ffmpeg результат дописывается в конец, если в шаблоне нет {out}
/// (кадры в папку: «{out}/{frame}-%03d.jpg»), а вход «-i файл» — в начало, если в шаблоне нет {in}
/// (с {in} параметры входа пишутся до него: «-ss {start} -to {end} -i {in}»).
/// </summary>
public static class OperationCatalog
{
    private const OperationMode Conv = OperationMode.Convert, Tool = OperationMode.Tool;

    private static readonly string[] RasterKeep = ["jpg", "jpeg", "png", "webp", "bmp", "tif", "tiff", "gif"];

    // Чётные размеры кадра обязательны для yuv420p.
    private const string H264 =
        "-c:v libx264 -preset medium -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart";
    private const string Even = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
    // Короткая сторона → не больше 720, пропорции сохраняются, размеры чётные.
    private const string To720 =
        "scale=w='trunc(iw*min(720,min(iw,ih))/min(iw,ih)/2)*2':h='trunc(ih*min(720,min(iw,ih))/min(iw,ih)/2)*2'";

    public static readonly IReadOnlyList<Operation> All =
    [
        // Изображения — конвертация
        new("to-jpg",  "JPG",  Conv, Image, ImageMagick, "-auto-orient -quality {jpgq}",  OutputRule.Fixed("jpg")),
        new("to-png",  "PNG",  Conv, Image, ImageMagick, "-auto-orient",                  OutputRule.Fixed("png")),
        new("to-webp", "WebP", Conv, Image, ImageMagick, "-auto-orient -quality {webpq}", OutputRule.Fixed("webp")),
        new("to-avif", "AVIF", Conv, Image, ImageMagick, "-auto-orient -quality 70",      OutputRule.Fixed("avif")),
        new("to-pdf",  "PDF",  Conv, Image, ImageMagick, "-auto-orient -compress jpeg -quality {jpgq}",OutputRule.Fixed("pdf")),
        // Изображения — инструменты
        new("img-compress", "Сжать",          Tool, Image, ImageMagick, "-auto-orient -quality 80",               OutputRule.Same("jpg", "jpg", "jpeg", "webp"), " (сжато)"),
        new("img-half",     "Уменьшить ×2",   Tool, Image, ImageMagick, "-auto-orient -resize 50%",               OutputRule.Same("jpg", RasterKeep), " (50%)"),
        new("img-1920",     "До 1920 px",     Tool, Image, ImageMagick, "-auto-orient -resize 1920x1920>",        OutputRule.Same("jpg", RasterKeep), " (1920)"),
        new("img-strip",    "Без метаданных", Tool, Image, ImageMagick, "-auto-orient -strip",                    OutputRule.Same("jpg", RasterKeep), " (без метаданных)"),
        // Обрезать (D-CROP): область {x} {y} {w} {h} — в пикселях уже повёрнутой (-auto-orient) картинки, задаётся
        // в окне «Обрезать…» или в CLI (--crop WxH+X+Y). +repage — без «холста» от исходника (иначе PNG/GIF помнят смещение).
        new(ImageCrop.Id,   "Обрезать…",      Tool, Image, ImageMagick, "-auto-orient -crop {w}x{h}+{x}+{y} +repage -quality {jpgq}",
            OutputRule.Same("jpg", RasterKeep), " (обрезано)") { Parameters = ImageCrop.ParameterNames, MaxFiles = 1 },

        // Видео — конвертация
        new("to-mp4",       "MP4",  Conv, Video, FFmpeg, $"-map 0:v:0 -map 0:a:0? -vf {Even} -crf {{crf}} {H264}", OutputRule.Fixed("mp4")),
        new("to-webm",      "WebM", Conv, Video, FFmpeg, "-map 0:v:0 -map 0:a:0? -c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -c:a libopus -b:a 128k", OutputRule.Fixed("webm")),
        new("to-gif",       "GIF",  Conv, Video, FFmpeg, "-an -vf fps=15,scale='min(640,iw)':-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse -loop 0", OutputRule.Fixed("gif")),
        new("video-to-mp3", "MP3",  Conv, Video, FFmpeg, "-vn -map 0:a:0 {acodec}", OutputRule.Fixed("mp3")),
        // Обложка: кадр на 1-й секунде. tpad дублирует последний кадр на секунду — у ролика короче секунды
        // берётся последний кадр, а не «ничего». -update 1 — один файл, а не серия.
        new("video-poster", "JPG (кадр)", Conv, Video, FFmpeg, "-vf tpad=stop_mode=clone:stop_duration=1 -ss 1 -frames:v 1 -q:v 2 -update 1", OutputRule.Fixed("jpg"), " (кадр)"),
        // Видео — инструменты
        new("video-compress", "Сжать",      Tool, Video, FFmpeg, $"-map 0:v:0 -map 0:a:0? -vf {Even} -crf {{crfsmall}} {H264}", OutputRule.Fixed("mp4"), " (сжато)"),
        new("video-720p",     "720p",       Tool, Video, FFmpeg, $"-map 0:v:0 -map 0:a:0? -vf {To720} -crf {{crf}} {H264}",     OutputRule.Fixed("mp4"), " (720p)"),
        new("video-mute",     "Без звука",  Tool, Video, FFmpeg, "-map 0 -map -0:a -c copy",                                     OutputRule.Same("mp4", "mp4", "m4v", "mov", "mkv", "webm", "avi"), " (без звука)"),
        // Повернуть на 90° по часовой: кадр перекодируется (H.264, как «Сжать»), звук — в AAC:
        // копировать звук нельзя — PCM/FLAC из MOV/MKV в MP4 не кладётся. Телефонная пометка поворота
        // ffmpeg применяет сам до transpose, так что «на боку» не окажется.
        new("video-rotate-cw", "Повернуть ↻", Tool, Video, FFmpeg, $"-map 0:v:0 -map 0:a:0? -vf transpose=1,{Even} -crf {{crf}} {H264}", OutputRule.Fixed("mp4"), " (повёрнуто)"),
        // Обрезать (D-TRIM): начало и конец ({start}/{end}, секунды) задаются в окне «Обрезать…» или в CLI.
        // -ss/-to до -i — поиск по входу. Без пересжатия: мгновенно и без потерь, контейнер тот же, все дорожки;
        // начало — с ближайшего опорного кадра не позже {start}. Точно по кадру — вариант с пересжатием в H.264
        // (как «Повернуть»: CRF из настроек, звук AAC), в колесе отдельно не показывается — это галочка окна.
        new(VideoTrim.Id, "Обрезать…", Tool, Video, FFmpeg, "-ss {start} -to {end} -i {in} -map 0 -c copy -avoid_negative_ts make_zero",
            OutputRule.Same("mp4", [.. Extensions.Video]), " (обрезано)") { Parameters = VideoTrim.ParameterNames, MaxFiles = 1 },
        new(VideoTrim.ExactId, "Обрезать точно", Tool, Video, FFmpeg, $"-ss {{start}} -to {{end}} -i {{in}} -map 0:v:0 -map 0:a:0? -vf {Even} -crf {{crf}} {H264}",
            OutputRule.Fixed("mp4"), " (обрезано)") { Parameters = VideoTrim.ParameterNames, MaxFiles = 1, OnWheel = false },
        // Кадр в секунду, не больше 300, в папку «видео (кадры)»: кадр-001.jpg … ({frame} — «кадр» / «frame»).
        new("video-frames", "Кадры", Tool, Video, FFmpeg, "-vf fps=1 -frames:v 300 -q:v 2 {out}/{frame}-%03d.jpg", OutputRule.Fixed("jpg"), " (кадры)") { Result = ResultKind.Folder },

        // Аудио — конвертация
        new("to-mp3",  "MP3",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("mp3")),
        new("to-m4a",  "M4A",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("m4a")),
        new("to-wav",  "WAV",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("wav")),
        new("to-flac", "FLAC", Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("flac")),
        new("to-ogg",  "OGG",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("ogg")),
        // Аудио — инструменты (loudnorm внутри работает на 192 кГц — возвращаем 48 кГц)
        new("audio-normalize", "Нормализовать", Tool, Audio, FFmpeg, "-vn -af loudnorm=I=-16:TP=-1.5:LRA=11 -ar 48000 {acodec}",
            OutputRule.Same("mp3", "mp3", "m4a", "wav", "flac", "ogg", "opus"), " (норм)"),

        // PDF (qpdf, pdftoppm из poppler). Страницы в папке: стр-1.png … или стр-01.png при 10+ страницах.
        new("pdf-to-png",    "PNG (страницы)", Conv, Pdf, Pdftoppm, "-r 150 -png {in} {out}/{page}",                        OutputRule.Fixed("png"), " (страницы)") { Result = ResultKind.Folder },
        new("pdf-to-jpg",    "JPG (страницы)", Conv, Pdf, Pdftoppm, "-r 150 -jpeg -jpegopt quality=90 {in} {out}/{page}",   OutputRule.Fixed("jpg"), " (страницы)") { Result = ResultKind.Folder },
        new("pdf-merge",     "Склеить",        Tool, Pdf, Qpdf, "--empty --pages {ins} -- {out}",                        OutputRule.Fixed("pdf"), " (объединено)") { Inputs = InputMode.Combine, MinFiles = 2 },
        new("pdf-split",     "Разбить",        Tool, Pdf, Qpdf, "--split-pages {in} {out}/{page}.pdf",                      OutputRule.Fixed("pdf"), " (по страницам)") { Result = ResultKind.Folder },
        new("pdf-optimize",  "Оптимизировать", Tool, Pdf, Qpdf, "--object-streams=generate --compress-streams=y --recompress-flate --compression-level=9 {in} {out}", OutputRule.Fixed("pdf"), " (оптим.)"),
        // Картинки → один PDF: каждая картинка — страница.
        new("images-to-pdf", "Один PDF",       Tool, Image, ImageMagick, "-auto-orient -compress jpeg -quality {jpgq}", OutputRule.Fixed("pdf"), " (все)") { Inputs = InputMode.Combine, MinFiles = 2 },
        // ZIP (встроенный движок ZipEngine). «В ZIP» — для любых файлов, все выбранные в один архив;
        // «Распаковать» — в папку с именем архива (photos.zip → photos, занято → photos (1)).
        new(Engines.ZipEngine.PackId, "В ZIP", Tool, Any, Zip, "", OutputRule.Fixed("zip"))
            { Inputs = InputMode.Combine, ResultStem = Engines.ZipEngine.ArchiveName },
        new(Engines.ZipEngine.ExtractId, "Распаковать", Tool, Archive, Zip, "", OutputRule.Fixed("zip"))
            { Result = ResultKind.Folder, InputExtensions = ["zip"] },

        // EXTRAS
        // Значок: картинка вписывается в прозрачный квадрат 256×256, в ICO — размеры 256…16.
        new("img-ico", "ICO (значок)", Conv, Image, ImageMagick,
            "-auto-orient -background none -resize 256x256 -gravity center -extent 256x256 -define icon:auto-resize=256,128,64,48,32,16",
            OutputRule.Fixed("ico")),
        // Распознать текст (OCR Windows, встроенный движок) — только форматы, которые Windows открывает сама.
        // GIF не берём: у него колесо уже заполнено (+ MP4/WebM ниже), а текст в GIF — редкость.
        new(OcrId, "Текст (OCR)", Conv, Image, Ocr, "", OutputRule.Fixed("txt"))
            { InputExtensions = ["jpg", "jpeg", "jfif", "png", "bmp", "tif", "tiff"] },
        // Анимированный GIF → видео (D8): без звука, чётные размеры кадра, yuv420p — иначе не играют плееры.
        new("gif-to-mp4",  "MP4",  Conv, Image, FFmpeg,
            $"-map 0:v:0 -an -vf {Even} -c:v libx264 -preset medium -pix_fmt yuv420p -crf {{crf}} -movflags +faststart",
            OutputRule.Fixed("mp4")) { InputExtensions = ["gif"] },
        new("gif-to-webm", "WebM", Conv, Image, FFmpeg,
            $"-map 0:v:0 -an -vf {Even} -c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -pix_fmt yuv420p",
            OutputRule.Fixed("webm")) { InputExtensions = ["gif"] },
        // Первая дорожка субтитров → .srt без пометки: плееры сами подхватывают «фильм.srt» рядом с «фильм.mkv».
        new("video-subs", "Субтитры SRT", Tool, Video, FFmpeg, "-map 0:s:0 -c:s srt", OutputRule.Fixed("srt")),
        // Перекодировать текст из Windows-1251 / DOS-866 / UTF-16 в UTF-8 (встроенный TextEngine).
        new(Engines.TextEngine.ToUtf8Id, "В UTF-8", Tool, Any, EngineKind.Text, "",
            OutputRule.Same("txt", [.. Engines.TextEngine.InputExtensions]), " (UTF-8)")
            { InputExtensions = Engines.TextEngine.InputExtensions },

        // 7ZIP-PDFX
        // 7-Zip: распаковать 7z/RAR/TAR/GZ/BZ2/XZ в папку с именем архива (.tar.gz — сразу файлы, см. SevenZip);
        // -p с выдуманным паролем — чтобы 7-Zip не ждал ввода пароля, а сразу сообщил «Wrong password».
        new(Engines.SevenZip.ExtractId, "Распаковать", Tool, Archive, SevenZip, "x -y -bso0 -bsp1 -sccUTF-8 -o{out} -pnopassword -- {in}", OutputRule.Fixed("7z"))
            { Result = ResultKind.Folder, InputExtensions = [.. Extensions.Archive.Except(["zip"])], ResultStem = Engines.SevenZip.FolderName },
        // В 7Z: записи — по именам файлов (без папок), имя архива — как у «В ZIP».
        new(Engines.SevenZip.PackId, "В 7Z", Tool, Any, SevenZip, "a -t7z -mx=7 -bso0 -bsp1 -sccUTF-8 -spd -sse {out} -- {ins}", OutputRule.Fixed("7z"))
            { Inputs = InputMode.Combine, ResultStem = Engines.ZipEngine.ArchiveName },
        // PDF: текст (poppler pdftotext; у скана текста нет — ошибка, см. PdfText), повернуть все страницы (qpdf).
        new("pdf-to-txt", "TXT (текст)", Conv, Pdf, Pdftotext, "-layout -enc UTF-8 {in} {out}", OutputRule.Fixed("txt")),
        new("pdf-rotate", "Повернуть ↻", Tool, Pdf, Qpdf, "--rotate=+90 {in} {out}", OutputRule.Fixed("pdf"), " (повёрнуто)"),

        // OFFICE — Word / Excel / PowerPoint через COM (OfficeEngine), только если Office установлен.
        // Что именно делает Office для операции (коды форматов) — Engines.OfficeFormats.
        // Запасные движки без Office: Word → PDF — Edge (WebPdf, только DOCX); Word → TXT и Excel → CSV — DocLibs.
        new("word-to-pdf",   "PDF",  Conv, Document, Office, "", OutputRule.Fixed("pdf"))  { InputExtensions = Extensions.Word, Fallback = WebPdf },
        new("word-to-docx",  "DOCX", Conv, Document, Office, "", OutputRule.Fixed("docx")) { InputExtensions = Extensions.Word },
        new("word-to-doc",   "DOC",  Conv, Document, Office, "", OutputRule.Fixed("doc"))  { InputExtensions = Extensions.Word },
        new("word-to-odt",   "ODT",  Conv, Document, Office, "", OutputRule.Fixed("odt"))  { InputExtensions = Extensions.Word },
        new("word-to-rtf",   "RTF",  Conv, Document, Office, "", OutputRule.Fixed("rtf"))  { InputExtensions = Extensions.Word },
        new("word-to-txt",   "TXT",  Conv, Document, Office, "", OutputRule.Fixed("txt"))  { InputExtensions = Extensions.Word, Fallback = DocLibs },
        // Excel: PDF — вся книга; CSV — первый лист, UTF-8, разделитель из региональных настроек Windows.
        new("excel-to-pdf",  "PDF",  Conv, Document, Office, "", OutputRule.Fixed("pdf"))  { InputExtensions = Extensions.Excel },
        // CSV → XLSX — не здесь, а в DOCLIBS (csv-to-xlsx): сам угадывает разделитель и кодировку, коды «007» не портит.
        new("excel-to-xlsx", "XLSX", Conv, Document, Office, "", OutputRule.Fixed("xlsx")) { InputExtensions = [.. Extensions.Excel.Except(["csv"])] },
        new("excel-to-xls",  "XLS",  Conv, Document, Office, "", OutputRule.Fixed("xls"))  { InputExtensions = Extensions.Excel },
        new("excel-to-csv",  "CSV",  Conv, Document, Office, "", OutputRule.Fixed("csv"))  { InputExtensions = Extensions.Excel, Fallback = DocLibs },
        new("excel-to-ods",  "ODS",  Conv, Document, Office, "", OutputRule.Fixed("ods"))  { InputExtensions = Extensions.Excel },
        // PowerPoint. «Слайды PNG» — папка «доклад (слайды)» с файлами слайд-1.png… (1920 px по ширине).
        new("ppt-to-pdf",    "PDF",  Conv, Document, Office, "", OutputRule.Fixed("pdf"))  { InputExtensions = Extensions.PowerPoint },
        new("ppt-to-pptx",   "PPTX", Conv, Document, Office, "", OutputRule.Fixed("pptx")) { InputExtensions = Extensions.PowerPoint },
        new("ppt-to-ppt",    "PPT",  Conv, Document, Office, "", OutputRule.Fixed("ppt"))  { InputExtensions = Extensions.PowerPoint },
        new("ppt-to-odp",    "ODP",  Conv, Document, Office, "", OutputRule.Fixed("odp"))  { InputExtensions = Extensions.PowerPoint },
        new("ppt-to-png",    "Слайды PNG", Conv, Document, Office, "", OutputRule.Fixed("png"), " (слайды)")
            { InputExtensions = Extensions.PowerPoint, Result = ResultKind.Folder },
        // PDF → Word: Word сам переводит PDF в редактируемый документ.
        new("pdf-to-docx",   "DOCX", Conv, Pdf, Office, "", OutputRule.Fixed("docx")),
        // PDF → Markdown: Word перестраивает PDF во временный DOCX, из него — Markdown (как word-to-md).
        // Без Word — текст pdftotext как есть (шаблон аргументов — для запасного движка). Решение D-OFFICE.
        new(Engines.OfficeFormats.PdfToMdId, "Markdown", Conv, Pdf, Office, "-layout -enc UTF-8 {in} {out}", OutputRule.Fixed("md"))
            { Fallback = Pdftotext },

        // DOCLIBS — документы без Office (решение D-DOCLIBS): бесплатные библиотеки (DocLibsEngine)
        // и печать HTML в PDF через Edge (WebPdfEngine, только Windows). Word — только DOCX/DOCM.
        new(Engines.DocLibsEngine.WordToHtmlId, "HTML",     Conv, Document,       DocLibs, "", OutputRule.Fixed("html")) { InputExtensions = Engines.DocLibsEngine.WordInputs },
        new(Engines.DocLibsEngine.WordToMdId,   "Markdown", Conv, Document,       DocLibs, "", OutputRule.Fixed("md"))   { InputExtensions = Engines.DocLibsEngine.WordInputs },
        new(Engines.DocLibsEngine.CsvToXlsxId,  "XLSX",     Conv, Document,       DocLibs, "", OutputRule.Fixed("xlsx")) { InputExtensions = Engines.DocLibsEngine.CsvInputs },
        new(Engines.DocLibsEngine.MdToHtmlId,   "HTML",     Conv, MediaKind.Text, DocLibs, "", OutputRule.Fixed("html")) { InputExtensions = Engines.DocLibsEngine.MarkdownInputs },
        new("text-to-pdf",                      "PDF",      Conv, MediaKind.Text, WebPdf,  "", OutputRule.Fixed("pdf")),
    ];

    /// <summary>Id операции OCR: движок живёт в WPF-приложении (Windows.Media.Ocr), каталог — здесь.</summary>
    public const string OcrId = "img-ocr";

    public static Operation? Find(string id) =>
        All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Операции для одного файла (по расширению) — те, что предлагает колесо (без вариантов OnWheel = false).</summary>
    /// <param name="available">Доступна ли операция для файла с этим расширением на этом компьютере
    /// (Converter.CanRun); null — не проверять.</param>
    public static IReadOnlyList<Operation> ForFile(string path, Func<Operation, string, bool>? available = null)
    {
        var ext = Path.GetExtension(path);
        return All.Where(o => o.OnWheel && o.AppliesTo(ext) && (available is null || available(o, ext))).ToList();
    }

    /// <summary>
    /// Операции, общие для всех файлов (с учётом MinFiles). Пустой список — «Нечего предложить».
    /// </summary>
    public static IReadOnlyList<Operation> ForFiles(IEnumerable<string> paths, Func<Operation, string, bool>? available = null)
    {
        IEnumerable<Operation>? common = null;
        var count = 0;
        foreach (var path in paths)
        {
            count++;
            var ops = ForFile(path, available);
            common = common is null ? ops : common.Intersect(ops);
        }
        return common?.Where(o => count >= o.MinFiles && count <= o.MaxFiles).ToList() ?? [];
    }
}
