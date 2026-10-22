using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

public class AutoStartRegistrationTests
{
    private const string Exe = @"C:\Программы\Drop Wheel\DropWheel.exe";

    /// <summary>На Linux разделитель путей — «/»: тест проверяет разбор команды, а не Windows.</summary>
    private static string P(string path) => OperatingSystem.IsWindows() ? path : path.Replace('\\', '/');

    [Fact]
    public void Command_quotes_path_and_adds_autostart_flag()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", AutoStartRegistration.RunKey);
        Assert.Equal("DropWheel", AutoStartRegistration.ValueName);
        Assert.Equal("\"C:\\Программы\\Drop Wheel\\DropWheel.exe\" --autostart", AutoStartRegistration.Command(Exe));
    }

    [Theory]
    [InlineData("\"C:\\Программы\\Drop Wheel\\DropWheel.exe\" --autostart", true)]
    [InlineData("\"c:\\программы\\drop wheel\\dropwheel.EXE\"", true)]
    [InlineData("\"D:\\Old\\DropWheel.exe\" --autostart", false)]
    [InlineData("C:\\Программы\\Drop Wheel\\DropWheel.exe --autostart", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Points_to_compares_exe_path_ignoring_case(string? command, bool expected) =>
        Assert.Equal(expected, AutoStartRegistration.PointsTo(command is null ? null : P(command), P(Exe)));

    [Fact]
    public void Exe_of_command()
    {
        Assert.Equal(@"C:\a\DropWheel.exe", AutoStartRegistration.ExeOf(@"C:\a\DropWheel.exe --autostart"));
        Assert.Equal(@"C:\a b\x.exe", AutoStartRegistration.ExeOf("\"C:\\a b\\x.exe\" -y"));
        Assert.Null(AutoStartRegistration.ExeOf("\"broken"));
    }
}
