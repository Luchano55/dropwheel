using DropWheel.App;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>Окно пароля PDF (D-PDFPASS) → PNG: «Защитить» (светлая, английская), «Снять пароль» (тёмная).</summary>
public class PasswordScreenshots
{
    internal static void Save(PasswordView view, bool light, string name) =>
        TrimScreenshots.SaveWindow(view, PasswordView.DefaultWidth, view.Title, view.Palette, light, name);

    internal static PasswordView MakeProtect(bool light, string file = "Договор аренды 2026.pdf", string password = "тайна 2026")
    {
        var view = new PasswordView(light ? SettingsPalette.Light() : SettingsPalette.Dark(), file, protect: true);
        view.SetPasswords(password, password);
        return view;
    }

    [Fact]
    public void Protect_window()
    {
        Sta.Run(() =>
        {
            var view = MakeProtect(light: true);
            Assert.True(view.CanConfirm);
            Assert.False(view.ShowsMismatch);
            Save(view, light: true, "pdf-password-light");
        });
    }

    [Fact]
    public void Unlock_window()
    {
        Sta.Run(() =>
        {
            var view = new PasswordView(SettingsPalette.Dark(), "Договор аренды 2026 (защищён).pdf", protect: false);
            Assert.False(view.CanConfirm);
            view.SetPasswords("тайна 2026");
            Assert.True(view.CanConfirm);
            Save(view, light: false, "pdf-password-dark");
        });
    }

    /// <summary>Повтор не совпал — «Пароли не совпадают», кнопка недоступна; совпал — пароль уходит событием, поля пустеют.</summary>
    [Fact]
    public void Protect_needs_matching_passwords_and_gives_password_once()
    {
        Sta.Run(() =>
        {
            var view = new PasswordView(SettingsPalette.Light(), "тест 1.pdf", protect: true);
            string? got = null;
            view.Confirmed += p => got = p;

            view.SetPasswords("тайна", "тайн");
            Assert.True(view.ShowsMismatch);
            Assert.False(view.CanConfirm);
            view.RequestConfirm();
            Assert.Null(got);
            Save(view, light: true, "pdf-password-light-mismatch");

            view.SetPasswords("тайна", "тайна");
            view.Reveal = true; // «Показать пароль» — текст не теряется
            Assert.True(view.CanConfirm);
            view.RequestConfirm();
            Assert.Equal("тайна", got);
            Assert.False(view.CanConfirm); // пароль забыт
        });
    }
}

/// <summary>Окно пароля по-английски (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class PasswordEnglishScreenshots
{
    [Fact]
    public void Protect_window_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            var view = PasswordScreenshots.MakeProtect(light: true, "Lease agreement 2026.pdf", "secret 2026");
            view.Reveal = true;
            PasswordScreenshots.Save(view, light: true, "pdf-password-light-en");
        });
    }
}
