using System.Net;
using System.Text;
using Markdig;

namespace DropWheel.Documents;

/// <summary>
/// Готовые HTML-страницы из текста и Markdown: для «Markdown → HTML» и для печати в PDF через Edge.
/// Страница самодостаточная: стили внутри, внешних ссылок нет.
/// </summary>
public static class HtmlPages
{
    /// <summary>Поля для TXT/Markdown/HTML при печати в PDF.</summary>
    public static readonly PageSetup TextPage = PageSetup.A4(18);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions() // таблицы, списки задач, сноски, автоссылки, зачёркивание…
        .Build();

    public static string MarkdownToHtmlBody(string markdown) => Markdown.ToHtml(markdown, Pipeline);

    /// <param name="baseUri">Папка исходника (file:///…/) — чтобы при печати из временной папки
    /// нашлись картинки с относительными путями. null — без &lt;base&gt;.</param>
    public static string FromMarkdown(string markdown, string title, string? baseUri = null) =>
        Page(title, MarkdownCss, MarkdownToHtmlBody(markdown), baseUri, "markdown-body");

    /// <summary>Простой текст как есть: моноширинный шрифт, переносы длинных строк, табуляции сохраняются.</summary>
    public static string FromPlainText(string text, string title) =>
        Page(title, PlainTextCss, "<pre>" + Encode(text.Replace("\0", "")) + "</pre>\n", null, null);

    public static string Encode(string text) => WebUtility.HtmlEncode(text);

    /// <summary>
    /// file:///C:/Мои%20фото/… — каждая часть пути экранирована (пробелы, кириллица, #, %).
    /// Папка — со слэшем на конце, если addTrailingSlash.
    /// </summary>
    public static string FileUri(string path, bool addTrailingSlash = false)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        string uri;
        if (full.StartsWith("//", StringComparison.Ordinal))
        {
            // UNC: \\server\share\file → file://server/share/file
            var parts = full.TrimStart('/').Split('/');
            uri = "file://" + parts[0] + "/" + string.Join("/", parts.Skip(1).Select(Uri.EscapeDataString));
        }
        else
        {
            var parts = full.TrimStart('/').Split('/');
            uri = "file:///" + string.Join("/", parts.Select((p, i) =>
                i == 0 && p.Length == 2 && p[1] == ':' ? p : Uri.EscapeDataString(p)));
        }
        if (addTrailingSlash && !uri.EndsWith('/')) uri += "/";
        return uri;
    }

    private static string Page(string title, string css, string body, string? baseUri, string? bodyClass)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        if (baseUri is not null) sb.Append("<base href=\"").Append(Encode(baseUri)).Append("\">\n");
        sb.Append("<title>").Append(Encode(title)).Append("</title>\n");
        sb.Append("<style>\n").Append(css).Append("</style>\n</head>\n");
        sb.Append(bodyClass is null ? "<body>\n" : $"<body class=\"{bodyClass}\">\n");
        sb.Append(body);
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    private static readonly string MarkdownCss = TextPage.CssPageRule + """

        html { color-scheme: light; }
        body {
          font-family: "Segoe UI", system-ui, -apple-system, Roboto, "Helvetica Neue", Arial, sans-serif;
          font-size: 11pt; line-height: 1.55; color: #1f2328; background: #fff;
          max-width: 820px; margin: 2rem auto; padding: 0 1rem;
          overflow-wrap: break-word;
        }
        @media print { body { max-width: none; margin: 0; padding: 0; } }
        h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.4em 0 .6em; font-weight: 600; break-after: avoid; }
        h1 { font-size: 1.9em; border-bottom: 1px solid #d1d9e0; padding-bottom: .25em; }
        h2 { font-size: 1.5em; border-bottom: 1px solid #d1d9e0; padding-bottom: .25em; }
        h3 { font-size: 1.25em; }
        h4 { font-size: 1.05em; }
        h5, h6 { font-size: 1em; color: #59636e; }
        body > :first-child { margin-top: 0; }
        p, ul, ol, dl, table, pre, blockquote { margin: 0 0 1em; }
        li + li { margin-top: .25em; }
        li > p { margin: 0 0 .5em; }
        a { color: #0969da; text-decoration: none; }
        code, pre, kbd { font-family: "Cascadia Mono", Consolas, "Courier New", monospace; font-size: .9em; }
        code { background: #eff1f3; padding: .15em .35em; border-radius: 4px; }
        pre { background: #f6f8fa; padding: .8em 1em; border-radius: 6px; white-space: pre-wrap; overflow-wrap: anywhere; break-inside: avoid; }
        pre code { background: none; padding: 0; font-size: 1em; }
        blockquote { color: #59636e; border-left: .25em solid #d1d9e0; padding: 0 1em; margin-left: 0; }
        table { border-collapse: collapse; display: block; max-width: 100%; overflow: auto; }
        @media print { table { display: table; } }
        th, td { border: 1px solid #d1d9e0; padding: .35em .7em; vertical-align: top; }
        th { background: #f6f8fa; font-weight: 600; }
        tr { break-inside: avoid; }
        img { max-width: 100%; height: auto; }
        hr { border: 0; border-top: 1px solid #d1d9e0; margin: 1.5em 0; }
        li.task-list-item { list-style: none; }
        li.task-list-item > input[type=checkbox] { margin: 0 .45em 0 -1.35em; vertical-align: middle; }

        """;

    private static readonly string PlainTextCss = TextPage.CssPageRule + """

        html { color-scheme: light; }
        body { margin: 1rem; background: #fff; color: #000; }
        @media print { body { margin: 0; } }
        pre {
          font-family: "Cascadia Mono", Consolas, "Courier New", monospace;
          font-size: 10pt; line-height: 1.35; tab-size: 4;
          white-space: pre-wrap; overflow-wrap: anywhere; margin: 0;
        }

        """;
}
