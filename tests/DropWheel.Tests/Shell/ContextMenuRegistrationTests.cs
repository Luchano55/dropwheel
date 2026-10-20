using DropWheel.Conversions;
using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

public class ContextMenuRegistrationTests
{
    private const string Exe = @"C:\Program Files\Мой DropWheel\DropWheel.exe";

    private static string KeyFor(string ext) =>
        $@"Software\Classes\SystemFileAssociations\.{ext}\shell\DropWheel.Convert";

    [Fact]
    public void Four_values_per_extension()
    {
        var entries = ContextMenuRegistration.Entries(Exe);
        Assert.Equal(Extensions.All.Count() * 4, entries.Count);
        Assert.Equal(entries.Count, entries.Distinct().Count());
    }

    [Fact]
    public void Every_extension_is_covered()
    {
        var entries = ContextMenuRegistration.Entries(Exe);
        var keys = ContextMenuRegistration.KeysToDelete();

        Assert.Equal(Extensions.All.Count(), keys.Count);
        foreach (var ext in Extensions.All)
        {
            var key = KeyFor(ext);
            Assert.Contains(key, keys);
            Assert.Contains(new RegistryValue(key, "MUIVerb", "Конвертировать…"), entries);
            Assert.Contains(new RegistryValue(key, "MultiSelectModel", "Player"), entries);
            Assert.Contains(entries, e => e.KeyPath == key && e.Name == "Icon");
            Assert.Contains(entries, e => e.KeyPath == key + @"\command" && e.Name is null);
        }
    }

    [Fact]
    public void Exe_path_with_spaces_and_cyrillic_is_quoted()
    {
        var entries = ContextMenuRegistration.Entries(Exe);
        var jpg = KeyFor("jpg");

        Assert.Equal("\"C:\\Program Files\\Мой DropWheel\\DropWheel.exe\",0",
            entries.Single(e => e.KeyPath == jpg && e.Name == "Icon").Value);
        Assert.Equal("\"C:\\Program Files\\Мой DropWheel\\DropWheel.exe\" --convert \"%1\"",
            entries.Single(e => e.KeyPath == jpg + @"\command").Value);
    }

    [Fact]
    public void Keys_are_relative_to_hkcu_with_leading_dot()
    {
        foreach (var e in ContextMenuRegistration.Entries(Exe))
        {
            Assert.StartsWith(@"Software\Classes\SystemFileAssociations\.", e.KeyPath);
            Assert.DoesNotContain("HKEY", e.KeyPath);
        }
    }
}
