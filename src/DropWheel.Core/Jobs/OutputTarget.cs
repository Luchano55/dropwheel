namespace DropWheel.Jobs;

/// <summary>
/// Куда и под каким именем положить результат одной задачи вместо обычного «рядом с исходником / в одну папку».
/// Нужен папкам целиком (решение D-FOLDERS): «Отпуск/2023/a.heic» → «Отпуск (JPG)/2023/a.jpg».
/// </summary>
/// <param name="Directory">Папка результата (создаётся при необходимости).</param>
/// <param name="Stem">Основа имени вместо имени исходника (Combine по папке — «Отпуск»); null — как обычно.</param>
/// <param name="KeepSuffix">Оставить пометку операции в имени (« (сжато)»). В папке «Отпуск (сжато)» она лишняя.</param>
/// <param name="CopySameFormat">
/// Файл уже в формате результата (photo.jpg и «JPG») — скопировать как есть, чтобы папка-результат была полной.
/// </param>
public sealed record OutputTarget(string Directory, string? Stem = null, bool KeepSuffix = true, bool CopySameFormat = false);
