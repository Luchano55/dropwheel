using DropWheel.Conversions;
using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

public class ContextMenuRegistrationTests
{
    private const string Exe = @"C:\Program Files\Мой DropWheel\DropWheel.exe";
    private const string AllFiles = @"Software\Classes\*\shell\DropWheel.Convert";

    private static string LegacyKeyFor(string ext) =>
        $@"Software\Classes\SystemFileAssociations\.{ext}\shell\DropWheel.Convert";

    [Fact]
    public void One_verb_for_all_files()
    {
        var entries = ContextMenuRegistration.Entries(Exe);
        Assert.Equal(4, entries.Count);
        Assert.Equal(entries.Count, entries.Distinct().Count());
        Assert.Contains(new RegistryValue(AllFiles, "MUIVerb", "Конвертировать…"), entries);
        Assert.Contains(new RegistryValue(AllFiles, "MultiSelectModel", "Player"), entries);
        Assert.Contains(entries, e => e.KeyPath == AllFiles && e.Name == "Icon");
        Assert.Contains(entries, e => e.KeyPath == AllFiles + @"\command" && e.Name is null);
    }

    [Fact]
    public void Folders_are_not_registered()
    {
        Assert.All(ContextMenuRegistration.Entries(Exe), e =>
        {
            Assert.DoesNotContain("Directory", e.KeyPath);
            Assert.DoesNotContain("AllFilesystemObjects", e.KeyPath);
        });
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

        Assert.Equal("\"C:\\Program Files\\Мой DropWheel\\DropWheel.exe\",0",
            entries.Single(e => e.Name == "Icon").Value);
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
