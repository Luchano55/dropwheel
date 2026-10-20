using System.Runtime.InteropServices;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Исходные документы для тестов Office создаёт сам Office (COM, отдельный невидимый экземпляр).
/// Каждый объект COM освобождаем — иначе Excel не выходит.
/// </summary>
internal static class OfficeSamples
{
    public static string Word(string path, string text, string? password = null, int format = 16 /* docx */)
    {
        Sta.Run(() =>
        {
            dynamic app = Create("Word.Application");
            dynamic? docs = null, doc = null, content = null;
            try
            {
                app.Visible = false;
                app.DisplayAlerts = 0;
                docs = app.Documents;
                doc = docs.Add();
                content = doc.Content;
                content.Text = "Заголовок\r" + text;
                if (password is null) doc.SaveAs2(FileName: path, FileFormat: format);
                else doc.SaveAs2(FileName: path, FileFormat: format, Password: password);
                doc.Close(SaveChanges: 0);
            }
            finally
            {
                Release((object?)content, (object?)doc, (object?)docs);
                app.Quit(SaveChanges: 0);
                Release((object)app);
            }
        });
        return path;
    }

    /// <summary>Книга: строки — на первом листе; второй лист активен при сохранении (CSV должен взять первый).</summary>
    public static string Excel(string path, string[][] rows, string? password = null, int format = 51 /* xlsx */)
    {
        Sta.Run(() =>
        {
            dynamic app = Create("Excel.Application");
            var used = new List<object>();
            T Keep<T>(T com) { used.Add(com!); return com; }
            try
            {
                app.Visible = false;
                app.DisplayAlerts = false;
                dynamic wb = Keep<dynamic>(Keep<dynamic>(app.Workbooks).Add());
                dynamic sheets = Keep<dynamic>(wb.Worksheets);
                dynamic first = Keep<dynamic>(sheets[1]);
                for (var r = 0; r < rows.Length; r++)
                    for (var c = 0; c < rows[r].Length; c++)
                        Keep<dynamic>(first.Cells[r + 1, c + 1]).Value2 = rows[r][c];
                dynamic second = Keep<dynamic>(sheets.Add(After: first));
                Keep<dynamic>(second.Cells[1, 1]).Value2 = "второй лист — не попадёт в CSV";
                second.Activate();
                if (password is null) wb.SaveAs(Filename: path, FileFormat: format);
                else wb.SaveAs(Filename: path, FileFormat: format, Password: password);
                wb.Close(SaveChanges: false);
            }
            finally
            {
                used.Reverse();
                Release([.. used]);
                app.Quit();
                Release((object)app);
            }
        });
        return path;
    }

    public static string PowerPoint(string path, params string[] titles)
    {
        Sta.Run(() =>
        {
            // PowerPoint однoэкземплярный: если Роман держит его открытым, мы в его же процессе — не закрываем.
            dynamic app = Create("PowerPoint.Application");
            var used = new List<object>();
            T Keep<T>(T com) { used.Add(com!); return com; }
            try
            {
                dynamic presentations = Keep<dynamic>(app.Presentations);
                dynamic pres = Keep<dynamic>(presentations.Add(WithWindow: 0));
                dynamic slides = Keep<dynamic>(pres.Slides);
                for (var i = 0; i < titles.Length; i++)
                {
                    dynamic slide = Keep<dynamic>(slides.Add(i + 1, 1 /* ppLayoutTitle */));
                    dynamic shapes = Keep<dynamic>(slide.Shapes);
                    dynamic title = Keep<dynamic>(shapes[1]);
                    Keep<dynamic>(Keep<dynamic>(title.TextFrame).TextRange).Text = titles[i];
                }
                pres.SaveAs(FileName: path, FileFormat: 24 /* pptx */);
                pres.Close();
                if ((int)presentations.Count == 0 && (int)app.Visible == 0) app.Quit();
            }
            finally
            {
                used.Reverse();
                Release([.. used]);
                Release((object)app);
            }
        });
        return path;
    }

    private static object Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

    private static void Release(params object?[] objects)
    {
        foreach (var com in objects)
            if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
