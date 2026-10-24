using DropWheel.Localization;
using DropWheel.Updates;
using DropWheel.Wheel;

namespace DropWheel.Ui;

/// <summary>
/// Подписи окна настроек и трея на текущем языке (решение D-LANG). Сами строки — в ядре
/// (DropWheel.Localization.Texts: русские и английские); здесь — короткие имена для окна настроек.
/// </summary>
internal static class Strings
{
    private static Texts T => Loc.T;

    public static class Tray
    {
        public static string Settings => T.TraySettings;
        public static string StartHint(string hotkey) => Loc.F(T.TrayStartHint, hotkey);
    }

    public static class Settings
    {
        public static string WindowTitle => T.SettingsTitle;

        // Разделы (левая колонка)
        public static string PageGeneral => T.PageGeneral;
        public static string PageQuality => T.PageQuality;
        public static string PageWheel => T.PageWheel;

        // Горячая клавиша
        public static string HotkeyTitle => T.HotkeyTitle;
        public static string HotkeyHint => T.HotkeyHint;
        public static string HotkeyCustom => T.HotkeyCustom;
        public static string HotkeyPress => T.HotkeyPress;
        public static string HotkeyModifier => T.HotkeyModifier;
        public static string HotkeyReserved(string key) => Loc.F(T.HotkeyReserved, key);
        public static string HotkeyTypes(string key) => Loc.F(T.HotkeyTypes, key);

        // Куда класть результат
        public static string OutputTitle => T.OutputTitle;
        public static string OutputBeside => T.OutputBeside;
        public static string OutputShared => T.OutputShared;
        public static string OutputBrowse => T.OutputBrowse;
        public static string OutputBrowseTitle => T.OutputBrowseTitle;

        // Язык
        public static string LanguageTitle => T.LanguageTitle;
        public static string LanguageHint => T.LanguageHint;
        public static string LanguageAuto => T.LanguageAuto;

        // Качество
        public static string QualityIntro => T.QualityIntro;
        public const string JpegTitle = "JPG";
        public static string JpegHint => T.JpegHint;
        public const string WebpTitle = "WebP";
        public static string WebpHint => T.WebpHint;
        public static string CrfTitle => T.CrfTitle;
        public static string CrfHint => T.CrfHint;
        public const string Mp3Title = "MP3";
        public static string Mp3Hint => T.Mp3Hint;
        public static string Kbps(int value) => Loc.F(T.Kbps, value);

        // Система
        public const string SystemTitle = "Windows";
        public static string AutoStart => T.AutoStart;
        public static string AutoStartHint => T.AutoStartHint;
        public static string ContextMenu => T.ContextMenu;
        public static string ContextMenuHint => T.ContextMenuHint;
        public static string ClipboardHotkey => T.ClipboardHotkey;
        public static string ClipboardHotkeyHint => T.ClipboardHotkeyHint;
        public static string ClipboardImageTitle => T.ClipboardImageTitle;
        public static string ClipboardImageHint => T.ClipboardImageHint;
        public static string ClipboardFolderDefault => T.ClipboardFolderDefault;
        public static string ClipboardFolderOwn => T.ClipboardFolderOwn;
        public static string ClipboardFolderBrowseTitle => T.ClipboardFolderBrowseTitle;
        public static string KeepClipboardImage => T.KeepClipboardImage;
        public static string KeepClipboardImageHint => T.KeepClipboardImageHint;
        public static string CopyResult => T.CopyResult;
        public static string CopyResultHint => T.CopyResultHint;
        public static string WheelOpacityTitle => T.WheelOpacityTitle;
        public static string WheelOpacityHint => T.WheelOpacityHint;
        public static string WheelOpacitySolid => T.WheelOpacitySolid;
        public static string WheelOpacityClear => T.WheelOpacityClear;
        public static string Updates => T.Updates;
        public static string UpdatesHint => T.UpdatesHint;
        public static string UpdateChannelTitle => T.UpdateChannelTitle;
        public static string UpdateChannelHint => T.UpdateChannelHint;
        public static string UpdateChannelStable => T.UpdateChannelStable;
        public static string UpdateChannelDev => T.UpdateChannelDev;
        public static string UpdateChannelAuto(UpdateChannel embedded) => Loc.F(T.UpdateChannelAuto,
            (embedded == UpdateChannel.Stable ? T.UpdateChannelStable : T.UpdateChannelDev).ToLowerInvariant());

        // Колесо
        public static string WheelIntro => T.WheelIntro;
        public static string MruFirst => T.MruFirst;
        public static string MruFirstHint => T.MruFirstHint;
        public static string ModeConvert => T.ModeConvert;
        public static string ModeTools => T.ModeTools;
        public static string Reset => T.Reset;
        public static string ResetHint => T.ResetHint;
        public static string MoveUp => T.MoveUp;
        public static string MoveDown => T.MoveDown;
        public static string ShowOnWheel => T.ShowOnWheel;
        public static string Preview => T.Preview;
        public static string OverLimit => T.OverLimit;
        public static string NeedsOffice => T.NeedsOffice;
        public static string OnlyFor(IEnumerable<string> extensions) =>
            Loc.F(T.OnlyFor, string.Join(", ", extensions.Select(e => e.ToUpperInvariant())));
        public static string FromFiles(int count) => Loc.F(T.FromFiles, count);
        public static string NothingInMode => T.NothingInMode;

        // Низ окна
        public static string OpenLogs => T.OpenLogs;
        public static string CheckUpdates => T.TrayCheckUpdates;
        public static string Version(int build, string? version = null) =>
            build <= 0 ? T.VersionDev
            : string.IsNullOrWhiteSpace(version) ? Loc.F(T.Version, build)
            : Loc.F(T.VersionWithBuild, version, build);

        public static string GroupName(string group) => group switch
        {
            WheelGroups.Photo => T.GroupPhoto,
            WheelGroups.Video => T.GroupVideo,
            WheelGroups.Audio => T.GroupAudio,
            WheelGroups.Pdf => "PDF",
            WheelGroups.Word => "Word",
            WheelGroups.Excel => "Excel",
            WheelGroups.PowerPoint => "PowerPoint",
            WheelGroups.Text => T.GroupText,
            WheelGroups.Archive => T.GroupArchive,
            _ => T.GroupAny,
        };
    }
}
