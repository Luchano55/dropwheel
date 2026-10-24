using DropWheel.Capture;

namespace DropWheel.Tests.Capture;

/// <summary>Сочетание снимка в настройках («Изменить…») и снимок с задержкой (D-CAPTURE).</summary>
public class ChordCheckTests
{
    private const uint Ctrl = KeyChord.Control, Alt = KeyChord.Alt, Shift = KeyChord.Shift, Win = KeyChord.Win;

    [Theory]
    [InlineData(Ctrl | Alt, 0x53, ChordCheck.Ok)]          // Ctrl+Alt+S
    [InlineData(Ctrl | Shift, 0x2C, ChordCheck.Ok)]        // Ctrl+Shift+PrintScreen
    [InlineData(Alt, 0x31, ChordCheck.Ok)]                 // Alt+1
    [InlineData(0u, 0x78, ChordCheck.Ok)]                  // F9 без модификаторов
    [InlineData(0u, 0x2C, ChordCheck.Ok)]                  // одна Print Screen
    [InlineData(Win, 0x78, ChordCheck.Ok)]                 // Win+F9 — можно
    [InlineData(Shift, 0x87, ChordCheck.Ok)]               // Shift+F24
    [InlineData(0u, 0x53, ChordCheck.NeedsModifier)]       // S
    [InlineData(Shift, 0x53, ChordCheck.NeedsModifier)]    // Shift+S — заглавная буква
    [InlineData(0u, 0x35, ChordCheck.NeedsModifier)]       // 5
    [InlineData(Win | Shift, 0x53, ChordCheck.WindowsOwned)] // Win+Shift+S — системный снимок
    [InlineData(Win, 0x56, ChordCheck.WindowsOwned)]       // Win+V — журнал буфера
    [InlineData(Win, 0x31, ChordCheck.WindowsOwned)]       // Win+1 — панель задач
    [InlineData(Win | Alt, 0x52, ChordCheck.WindowsOwned)] // Win+Alt+R — запись
    [InlineData(Win | Ctrl, 0x44, ChordCheck.WindowsOwned)] // Win+Ctrl+D — рабочие столы
    [InlineData(Win, 0x2C, ChordCheck.WindowsOwned)]       // Win+PrintScreen
    [InlineData(Ctrl, 0x20, ChordCheck.UnsupportedKey)]    // Ctrl+Space
    [InlineData(Ctrl | Alt, 0x0D, ChordCheck.UnsupportedKey)] // Enter
    [InlineData(Ctrl, 0x25, ChordCheck.UnsupportedKey)]    // стрелка
    [InlineData(Ctrl, 0xA2, ChordCheck.ModifierOnly)]      // левый Ctrl
    [InlineData(0u, 0x10, ChordCheck.ModifierOnly)]        // Shift
    [InlineData(Win, 0x5B, ChordCheck.ModifierOnly)]       // Win
    [InlineData(Alt, 0xA5, ChordCheck.ModifierOnly)]       // правый Alt
    public void Check(uint modifiers, int vk, ChordCheck expected) => Assert.Equal(expected, KeyChord.Check(modifiers, vk));

    /// <summary>Принятое сочетание записывается и читается обратно тем же (settings.json хранит строку).</summary>
    [Theory]
    [InlineData(Ctrl | Alt, 0x53, "Ctrl+Alt+S")]
    [InlineData(Ctrl | Shift, 0x2C, "Ctrl+Shift+PrintScreen")]
    [InlineData(Win, 0x78, "Win+F9")]
    [InlineData(0u, 0x7B, "F12")]
    public void Accepted_chord_round_trips(uint modifiers, int vk, string text)
    {
        Assert.Equal(ChordCheck.Ok, KeyChord.Check(modifiers, vk));
        var chord = new KeyChord(modifiers, vk);
        Assert.Equal(text, chord.ToString());
        Assert.Equal(chord, KeyChord.Parse(text));
    }

    [Fact]
    public void Countdown_steps()
    {
        Assert.Equal(["3…", "2…", "1…"], CaptureCountdown.Steps(3));
        Assert.Equal(["5…", "4…", "3…", "2…", "1…"], CaptureCountdown.Steps(5));
        Assert.Empty(CaptureCountdown.Steps(0));
        Assert.Empty(CaptureCountdown.Steps(-2));
        Assert.Equal(60, CaptureCountdown.Steps(500).Count);
        Assert.Equal([3, 5], CaptureCountdown.TrayDelays);
        Assert.All(CaptureCountdown.TrayDelays, d => Assert.Equal(d, CaptureCountdown.Steps(d).Count));
    }
}
