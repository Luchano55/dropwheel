# Как выпустить версию и добавить DropWheel в winget

Пошагово, для Романа. Техническое описание — решение D-RELEASE в `docs/decisions.md`.

Коротко: **тег `vX.Y.Z` → GitHub сам собирает установщик и выкладывает релиз → манифесты winget
лежат готовыми в релизе → их отправляем в Microsoft (один раз на каждую версию).**

---

## 1. Выпустить версию (например, 0.3.0)

Это можете сделать вы или попросить Claude («выпусти 0.3.0»).

1. Убедитесь, что в `main` всё проверено (последняя сборка зелёная:
   [Actions](https://github.com/Luchano55/dropwheel/actions/workflows/build.yml)).
2. В `docs/CHANGELOG.md` есть раздел `## 0.3.0` — его текст попадёт в описание релиза.
   В файле `version.txt` — `0.3.0`.
3. Проще всего — кнопкой на GitHub: [Actions → build](https://github.com/Luchano55/dropwheel/actions/workflows/build.yml)
   → **Run workflow** → ветка `main`, галочка **«Выпустить стабильную версию…»** → **Run workflow**.
   Сборка сама создаст тег `v<version.txt>` и релиз. (Так выпускает и Claude: из облачной сессии тег отправить нельзя.)

   Или тег из терминала (в папке репозитория):
   ```
   git checkout main
   git pull
   git tag v0.3.0
   git push origin v0.3.0
   ```
   Без терминала: на GitHub → **Releases** → **Draft a new release** → **Choose a tag** → впишите `v0.3.0`
   → **Create new tag: v0.3.0 on publish** → Target: `main` → **Publish release**. (Сборка сама заменит
   описание и добавит файлы. Если GitHub попросит название — впишите `DropWheel 0.3.0`.)
4. Подождите 20–30 минут, пока пройдёт сборка
   ([Actions](https://github.com/Luchano55/dropwheel/actions/workflows/build.yml) → запуск с именем `v0.3.0`).
5. Откройте [Releases](https://github.com/Luchano55/dropwheel/releases) — там появится **«DropWheel 0.3.0»**
   (обычный релиз, не «Pre-release») с файлами:
   - `DropWheel-Setup-0.3.0.exe` — установщик;
   - `DropWheel-win-x64.zip`, `DropWheel-app-win-x64.zip`, `DropWheel-tools-win-x64.zip`, `version.json` — архивы и файл для самообновления;
   - `DropWheel-winget-manifests-0.3.0.zip` — готовые файлы для winget (шаг 3).
6. Проверьте установщик на своём ПК (чек-лист «Установщик» в `docs/manual-tests.md`).

**SHA256 установщика** (длинная строка из букв и цифр, нужна winget) — в конце описания релиза,
а также в сводке запуска сборки (Actions → запуск → Summary) и в журнале шага «Установщик (Inno Setup)».

> Если что-то пошло не так (сборка красная) — исправляем, и можно запустить сборку тега ещё раз
> (Actions → запуск → **Re-run all jobs**): файлы в релизе заменятся. Номер версии менять не нужно.

**Следующая версия** (например, 0.3.1): поправить `version.txt` на `0.3.1`, добавить раздел `## 0.3.1`
в `docs/CHANGELOG.md`, затем тег `v0.3.1`. Папку `packaging/winget/0.3.1` создавать не обязательно —
сборка возьмёт последнюю имеющуюся как шаблон.

---

## 2. Что такое winget и зачем

winget — «магазин» программ в Windows 10/11 через командную строку. Когда DropWheel туда примут,
любой сможет поставить его одной командой:
```
winget install Luchano55.DropWheel
```
и обновлять вместе с другими программами (`winget upgrade --all`).

Чтобы попасть в winget, нужно отправить 4 маленьких файла-описания (манифеста) в репозиторий Microsoft
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Файлы сборка уже приготовила:

| Файл | Что в нём |
|---|---|
| `Luchano55.DropWheel.yaml` | номер версии |
| `Luchano55.DropWheel.installer.yaml` | ссылка на установщик и его SHA256 |
| `Luchano55.DropWheel.locale.en-US.yaml` | название, описание, лицензия (по-английски) |
| `Luchano55.DropWheel.locale.ru-RU.yaml` | то же по-русски |

Взять их: из релиза — `DropWheel-winget-manifests-0.3.0.zip` (внутри папки `manifests\l\Luchano55\DropWheel\0.3.0\`),
или Actions → запуск → внизу **Artifacts** → `winget-manifests`.

> Шаблоны этих файлов лежат в репозитории в `packaging/winget/0.3.0/`, но там вместо SHA256 стоит
> `<filled by CI>` — отправлять нужно файлы **из релиза**, а не из репозитория.

---

## 3. Отправить в winget

### Автоматически (настроено с 1.0.0)

Каждый стабильный выпуск сам отправляет заявку в winget — шаг «Отправка в winget» в сборке
(`build/submit-winget.ps1`, программа wingetcreate на сервере GitHub). Нужен один раз секрет с ключом:

1. https://github.com/settings/tokens → **Generate new token** → **Generate new token (classic)**;
   Note: `winget-auto`; Expiration: **No expiration**; одна галочка **`public_repo`** → **Generate token** → скопировать `ghp_…`.
2. https://github.com/Luchano55/dropwheel/settings/secrets/actions → **New repository secret** →
   Name: `WINGET_TOKEN`, Secret: вставить ключ → **Add secret**. Ключ больше никому не виден (и в журналах сборки скрыт).

Нет секрета — выпуск всё равно выходит, в журнале предупреждение. Отправить уже выпущенную версию вручную:
[Actions → winget](https://github.com/Luchano55/dropwheel/actions/workflows/winget.yml) → **Run workflow** → версия (`1.0.0`).
Заявки на новые версии уже принятой программы winget обычно принимает сам, без модератора.

Варианты ниже — если хочется отправить руками.


Нужен аккаунт GitHub (тот же, Luchano55). Выберите **один** из вариантов.

### Вариант А — программа wingetcreate (быстрее, 5 минут)

1. **Токен GitHub** (пароль для программ):
   - откройте https://github.com/settings/tokens → **Generate new token** → **Generate new token (classic)**;
   - Note: `wingetcreate`; Expiration: `30 days`;
   - поставьте одну галочку **`public_repo`**;
   - **Generate token** → скопируйте строку `ghp_…` (её покажут один раз; никому не отправляйте).
2. **Поставьте wingetcreate.** Откройте «Терминал» (правый клик по «Пуску» → «Терминал») и выполните:
   ```
   winget install Microsoft.WingetCreate
   ```
   Закройте и снова откройте терминал.
3. **Распакуйте** `DropWheel-winget-manifests-0.3.0.zip`, например в `Загрузки\winget`.
4. **Отправьте** (подставьте свой токен и путь):
   ```
   wingetcreate submit --token ghp_ВАШ_ТОКЕН "$env:USERPROFILE\Downloads\winget\manifests\l\Luchano55\DropWheel\0.3.0"
   ```
   Программа сама сделает копию репозитория Microsoft у вас на GitHub, загрузит файлы и откроет запрос
   (Pull Request). В конце покажет ссылку вида `https://github.com/microsoft/winget-pkgs/pull/12345` — сохраните её.
5. Токен после этого можно удалить (https://github.com/settings/tokens → Delete).

Для следующих версий проще так (wingetcreate сам скачает установщик и посчитает SHA256):
```
wingetcreate update Luchano55.DropWheel --version 0.3.1 --urls https://github.com/Luchano55/dropwheel/releases/download/v0.3.1/DropWheel-Setup-0.3.1.exe --submit --token ghp_ВАШ_ТОКЕН
```

### Вариант Б — через сайт GitHub, без программ

1. Откройте https://github.com/microsoft/winget-pkgs → кнопка **Fork** (вверху справа) → **Create fork**.
   Получится ваша копия `github.com/Luchano55/winget-pkgs`.
2. Распакуйте `DropWheel-winget-manifests-0.3.0.zip` — внутри папка `manifests`.
3. В вашей копии (на главной странице `Luchano55/winget-pkgs`): **Add file** → **Upload files** и перетащите
   в окно **всю папку `manifests`** целиком (из Edge или Chrome). GitHub сохранит вложенные папки.
   Проверьте в списке под окном: у всех 4 файлов путь начинается с `manifests/l/Luchano55/DropWheel/0.3.0/`.
   (Если браузер загрузил файлы без папок — отмените и используйте вариант А.)
4. Внизу выберите **Create a new branch for this commit** (имя, например, `DropWheel-0.3.0`) → **Propose changes**.
5. GitHub предложит **Create pull request** — нажмите. Вверху должно быть `base repository: microsoft/winget-pkgs`,
   `base: master` ← `head repository: Luchano55/winget-pkgs`, `compare: DropWheel-0.3.0`. Заголовок: `New package: Luchano55.DropWheel version 0.3.0`.
   В описании заполните чек-лист шаблона (галочки «[x]» — что вы прочитали правила, что манифест проверен).
   Нажмите **Create pull request**.

### Что дальше (для обоих вариантов)

- В запрос придёт **бот проверки** (wingetbot / Azure Pipelines). Он проверяет файлы по схеме, скачивает
  установщик, сверяет SHA256, проверяет его антивирусом и **ставит и удаляет DropWheel в тихом режиме**
  на чистой Windows. Это 30–90 минут. Результат — метки на запросе:
  - `Azure-Pipeline-Passed`, `Validation-Completed` — всё хорошо, ждём человека-модератора;
  - `Needs-Author-Feedback`, `Validation-…-Error` — бот что-то нашёл: в комментариях будет причина.
    Пришлите ссылку на запрос Claude — он поправит файлы.
- Затем модератор Microsoft одобряет — обычно **1–3 дня** (бывает до недели). После слияния пакет появляется
  в winget в течение нескольких часов: `winget search DropWheel`.
- Пока запрос открыт, ничего удалять/переименовывать в релизе нельзя (ссылка на установщик должна работать).

---

## 4. SmartScreen и антивирус

Установщик не подписан цифровой подписью (подпись стоит денег — для личного проекта это нормально).
Поэтому:
- **«Windows защитила ваш компьютер»** (SmartScreen) при первом запуске — нажмите **«Подробнее» →
  «Выполнить в любом случае»**. Предупреждение пропадает само, когда файл скачает достаточно людей.
  При установке через winget SmartScreen обычно не показывается.
- **Microsoft Defender удалил или заблокировал файл** (ложное срабатывание бывает у новых неподписанных
  программ, особенно с ffmpeg внутри):
  1. «Безопасность Windows» → «Защита от вирусов и угроз» → «Журнал защиты» — посмотреть, что именно сработало;
  2. отправить файл на проверку в Microsoft: https://www.microsoft.com/wdsi/filesubmission →
     «Software developer» → указать файл `DropWheel-Setup-0.3.0.exe`, отметить «Incorrectly detected as malware».
     Обычно ответ за 1–3 дня, после чего сигнатуры обновляются у всех;
  3. если такое случилось на проверке бота winget, модераторы сами подождут — в запросе ответьте ссылкой
     на отправку в Microsoft.
- Каждый релиз собирается GitHub Actions прямо из кода репозитория — это можно показать любому, кто спросит.

---

## 5. Что проверить после установки из winget

```
winget install Luchano55.DropWheel
```
- DropWheel появился в «Пуске», запускается, значок в трее.
- `winget list DropWheel` показывает версию 0.3.0.
- `winget uninstall Luchano55.DropWheel` — программа закрывается и удаляется, пункт «Конвертировать…»
  из меню проводника пропадает.
