using DropWheel.Wheel;

namespace DropWheel.Ui;

/// <summary>
/// Подписи окна настроек и связанных пунктов трея — все в одном месте, чтобы потом перевести интерфейс
/// на английский (раздел 15 ТЗ) одной правкой этого файла. Новые строки интерфейса — сюда.
/// </summary>
internal static class Strings
{
    public static class Tray
    {
        public const string Settings = "Настройки…";
        public static string StartHint(string hotkey) =>
            $"Значок — здесь, в трее. Потащите файл и нажмите {hotkey} — или правой кнопкой по файлу → «Конвертировать…».";
    }

    public static class Settings
    {
        public const string WindowTitle = "Настройки DropWheel";

        // Разделы (левая колонка)
        public const string PageGeneral = "Основное";
        public const string PageQuality = "Качество";
        public const string PageWheel = "Колесо";

        // Горячая клавиша
        public const string HotkeyTitle = "Горячая клавиша";
        public const string HotkeyHint =
            "Тащите файл и, не отпуская кнопку мыши, нажмите эту клавишу — появится колесо. Ещё раз — другой режим.";

        // Куда класть результат
        public const string OutputTitle = "Куда класть результат";
        public const string OutputBeside = "Рядом с исходником";
        public const string OutputShared = "В одну папку";
        public const string OutputBrowse = "Выбрать папку…";
        public const string OutputBrowseTitle = "Папка для результатов DropWheel";

        // Качество
        public const string QualityIntro = "Действует на новые задачи сразу. Уже идущие доделываются с прежними настройками.";
        public const string JpegTitle = "JPG";
        public const string JpegHint = "Качество фото в JPG и PDF из картинок.";
        public const string WebpTitle = "WebP";
        public const string WebpHint = "Качество картинок WebP.";
        public const string CrfTitle = "Видео (CRF)";
        public const string CrfHint = "Меньше — лучше качество и больше файл. 23 — обычно незаметно глазу.";
        public const string Mp3Title = "MP3";
        public const string Mp3Hint = "Битрейт звука в MP3: больше — лучше и больше файл.";
        public static string Kbps(int value) => $"{value} кбит/с";

        // Система
        public const string SystemTitle = "Windows";
        public const string AutoStart = "Запускать вместе с Windows";
        public const string AutoStartHint = "DropWheel тихо появится в трее после входа в Windows.";
        public const string ContextMenu = "Пункт «Конвертировать…» в проводнике";
        public const string ContextMenuHint = "Правый клик по файлу → «Конвертировать…» — то же колесо, но щелчком.";
        public const string Updates = "Проверять обновления";
        public const string UpdatesHint = "Раз в сутки — единственное обращение программы в интернет.";

        // Колесо
        public const string WheelIntro =
            "Какие форматы показывать и в каком порядке. Первый в списке — верхний лепесток, дальше — по часовой стрелке.";
        public const string MruFirst = "Последний выбор — первым";
        public const string MruFirstHint = "Формат, выбранный в прошлый раз для этого типа файлов, встаёт наверх.";
        public const string ModeConvert = "Конвертация";
        public const string ModeTools = "Инструменты";
        public const string Reset = "Сбросить";
        public const string ResetHint = "Вернуть порядок и видимость для этого типа как было";
        public const string MoveUp = "Выше";
        public const string MoveDown = "Ниже";
        public const string ShowOnWheel = "Показывать на колесе";
        public const string Preview = "Так будет выглядеть";
        public const string OverLimit = "На колесе помещается 8 — этот не виден";
        public const string NeedsOffice = "нужен Microsoft Office";
        public static string OnlyFor(IEnumerable<string> extensions) =>
            "только " + string.Join(", ", extensions.Select(e => e.ToUpperInvariant()));
        public static string FromFiles(int count) => $"от {count} файлов";
        public const string NothingInMode = "В этом режиме для этого типа ничего нет.";

        // Низ окна
        public const string OpenLogs = "Открыть папку журналов";
        public const string CheckUpdates = "Проверить обновления";
        public static string Version(int build) => build > 0 ? $"DropWheel, сборка {build}" : "DropWheel, сборка разработчика";

        public static string GroupName(string group) => group switch
        {
            WheelGroups.Photo => "Фото",
            WheelGroups.Video => "Видео",
            WheelGroups.Audio => "Звук",
            WheelGroups.Pdf => "PDF",
            WheelGroups.Word => "Word",
            WheelGroups.Excel => "Excel",
            WheelGroups.PowerPoint => "PowerPoint",
            WheelGroups.Text => "Текст",
            WheelGroups.Archive => "Архивы",
            _ => "Любые файлы",
        };
    }
}
