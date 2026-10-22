using DropWheel.Conversions;
using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

public class ContextMenuRegistrationTests
{
    private const string Exe = @"C:\Program Files\Мой DropWheel\DropWheel.exe";
    private const string AllFiles = @"Software\Classes\*\shell\DropWheel.Convert";
    private const string Folders = @"Software\Classes\Directory\shell\DropWheel.Convert";

    private static string LegacyKeyFor(string ext) =>
        $@"Software\Classes\SystemFileAssociations\.{ext}\shell\DropWheel.Convert";

    [Fact]
    public void One_verb_for_all_files_and_one_for_folders()
    {
        var entries = ContextMenuRegistration.Entries(Exe);
        Assert.Equal(8, entries.Count);
        Assert.Equal(entries.Count, entries.Distinct().Count());
        foreach (var key in new[] { AllFiles, Folders })
        {
            Assert.Contains(new RegistryValue(key, "MUIVerb", "Конвертировать…"), entries);
            Assert.Contains(new RegistryValue(key, "MultiSelectModel", "Player"), entries);
            Assert.Contains(entries, e => e.KeyPath == key && e.Name == "Icon");
            Assert.Contains(entries, e => e.KeyPath == key + @"\command" && e.Name is null);
        }
    }

    [Fact]
    public void Folders_are_registered_but_not_folder_background_or_drives()
    {
        Assert.Contains(ContextMenuRegistration.Entries(Exe), e => e.KeyPath == Folders);
        Assert.All(ContextMenuRegistration.Entries(Exe), e =>
        {
            Assert.DoesNotContain("Background", e.KeyPath);
            Assert.DoesNotContain(@"\Drive\", e.KeyPath);
            Assert.DoesNotContain("AllFilesystemObjects", e.KeyPath);
        });
        Assert.Equal("\"C:\\Program Files\\Мой DropWheel\\DropWheel.exe\" --convert \"%1\"",
            ContextMenuRegistration.Entries(Exe).Single(e => e.KeyPath == Folders + @"\command").Value);
    }

    [Fact]
    public void Unregister_removes_the_folder_verb_too()
    {
        Assert.Contains(Folders, ContextMenuRegistration.KeysToDelete());
        Assert.DoesNotContain(Folders, ContextMenuRegistration.LegacyKeys());
    }

    [Fact]
    public void Old_per_extension_keys_are_deleted_too()
    {
        var keys = ContextMenuRegistration.KeysToDelete();
        Assert.Contains(AllFiles, keys);
        foreach (var ext in Extensions.All)
        {
            Assert.Contains(LegacyKeyFor(ext), keys);
            Assert.Contains(LegacyKeyFor(ext), ContextMenuRegistration.LegacyKeys());
        }
        Assert.DoesNotContain(AllFiles, ContextMenuRegistration.LegacyKeys());
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Exe_path_with_spaces_and_cyrillic_is_quoted()
    {
        var entries = ContextMenuRegistration.Entries(Exe);

        Assert.All(entries.Where(e => e.Name == "Icon"), e =>
            Assert.Equal("\"C:\\Program Files\\Мой DropWheel\\DropWheel.exe\",0", e.Value));
        Assert.Equal("\"C:\\Program Files\\Мой DropWheel\\DropWheel.exe\" --convert \"%1\"",
            entries.Single(e => e.KeyPath == AllFiles + @"\command").Value);
    }

    [Fact]
    public void Keys_are_relative_to_hkcu()
    {
        foreach (var key in ContextMenuRegistration.KeysToDelete())
        {
            Assert.StartsWith(@"Software\Classes\", key);
            Assert.DoesNotContain("HKEY", key);
        }
    }
}
