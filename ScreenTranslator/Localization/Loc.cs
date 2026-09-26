using System.Collections.Generic;

namespace ScreenTranslator.Localization;

public enum AppLanguage
{
    Russian,
    English
}

/// <summary>
/// Small, dependency-free UI localization helper. Each window applies its own strings after
/// InitializeComponent() by calling <see cref="S"/> for every piece of user-facing text - there's
/// no live-switching magic here, changing the language takes effect the next time each window is
/// opened (the app itself suggests restarting after a language change for a fully consistent UI,
/// e.g. the tray menu/tooltip).
/// </summary>
public static class Loc
{
    public static AppLanguage Current { get; set; } = AppLanguage.Russian;

    public static string S(string key)
    {
        var table = Current == AppLanguage.English ? En : Ru;
        return table.TryGetValue(key, out var value) ? value : key;
    }

    private static readonly Dictionary<string, string> Ru = new()
    {
        // ===== Tray menu =====
        ["Tray.SelectRegion"] = "Выбрать область экрана",
        ["Tray.ToggleTranslation"] = "Включить / выключить перевод",
        ["Tray.TranslateOnce"] = "Перевести область один раз",
        ["Tray.HoverTranslate"] = "Мгновенный перевод",
        ["Tray.Settings"] = "Настройки...",
        ["Tray.Help"] = "Справка / с чего начать",
        ["Tray.About"] = "О программе",
        ["Tray.Exit"] = "Выход",

        // ===== Balloons / tray notifications =====
        ["Balloon.Started"] = "Программа запущена и работает в трее.",
        ["Balloon.StillRunning"] = "Программа осталась запущена в трее.",
        ["Balloon.HotkeyConflictTitle"] = "Конфликт горячих клавиш",
        ["Balloon.HotkeyConflict.SelectRegion"] = "Не удалось назначить «{0}» для выбора области — возможно, она уже занята другой программой.",
        ["Balloon.HotkeyConflict.Toggle"] = "Не удалось назначить «{0}» для включения/выключения перевода — возможно, она уже занята другой программой.",
        ["Balloon.HotkeyConflict.Settings"] = "Не удалось назначить «{0}» для открытия настроек — возможно, она уже занята другой программой.",
        ["Balloon.HotkeyConflict.OneTime"] = "Не удалось назначить «{0}» для разового перевода — возможно, она уже занята другой программой.",
        ["Balloon.HotkeyConflict.Retranslate"] = "Не удалось назначить «{0}» для повторного перевода — возможно, она уже занята другой программой.",
        ["Balloon.HotkeyConflict.HoverCombo"] = "Не удалось назначить «{0}» для мгновенного перевода — возможно, она уже занята другой программой.",
        ["Balloon.RegionSetTitle"] = "Область захвата выбрана",
        ["Balloon.RegionSetText"] = "{0}×{1} в точке ({2},{3}).",
        ["Balloon.OneTimeNoText"] = "Не удалось распознать текст в выбранной области.",
        ["Balloon.OneTimeError"] = "Ошибка разового перевода: {0}",
        ["Balloon.RetranslateError"] = "Ошибка повторного перевода: {0}",
        ["Balloon.HoverKeyInvalid"] = "Не удалось включить перевод по наведению: некорректная клавиша в настройках.",
        ["Warning.NoRegionText"] = "Сначала выберите область экрана для перевода (горячая клавиша «Выбрать область экрана»), а затем включите перевод.",

        // ===== About window =====
        ["About.Ocr"] = "Распознавание текста:",
        ["About.OcrValue"] = "Windows OCR, Tesseract, EasyOCR",
        ["About.Translation"] = "Перевод:",
        ["About.TranslationValue"] = "DeepL, Google Translate, Yandex Translate, Papago",
        ["About.Close"] = "Закрыть",

        // ===== Welcome window =====
        ["Welcome.Subtitle"] = "Перевод текста с экрана в реальном времени — для игр, субтитров и не только.",
        ["Welcome.TrayHeader"] = "Программа работает через значок в трее",
        ["Welcome.TrayBody"] = "Отдельного окна нет — приложение свернуто в значок рядом с часами (внизу справа, возможно под стрелочкой \"Показать скрытые значки\"). Кликните по нему правой кнопкой в любой момент, чтобы открыть меню.",
        ["Welcome.HotkeysHeader"] = "Горячие клавиши",
        ["Welcome.HotkeySelectRegion"] = "Выбрать область экрана",
        ["Welcome.HotkeyToggle"] = "Включить / выключить перевод",
        ["Welcome.HotkeyOnce"] = "Перевести область один раз (не меняя основную область)",
        ["Welcome.HotkeyOpenSettings"] = "Открыть настройки",
        ["Welcome.HotkeysFooter"] = "Клавиши можно изменить в настройках. Работают даже когда игра открыта на весь экран.",
        ["Welcome.GameTipHeader"] = "Совет для игр",
        ["Welcome.GameTipBody"] = "Запускайте игру в режиме «Оконный без рамки» (Borderless) — так перевод точно будет виден поверх игры. В режиме «Полноэкранный» (Fullscreen) окно перевода иногда может не отображаться. Обычно переключить режим можно клавишами Alt+Enter или в настройках графики игры.",
        ["Welcome.DefaultsHeader"] = "Что выбрать по умолчанию?",
        ["Welcome.DefaultsBody1"] = "Распознавание — ",
        ["Welcome.DefaultsBody1Bold"] = "Windows OCR",
        ["Welcome.DefaultsBody2"] = " (уже включено, работает из коробки). Перевод — ",
        ["Welcome.DefaultsBody2Bold"] = "Google Translate",
        ["Welcome.DefaultsBody3"] = " (тоже работает сразу, без ключа). Всё это можно поменять в настройках в любой момент.",
        ["Welcome.DontShowAgain"] = "Больше не показывать это окно",
        ["Welcome.Close"] = "Закрыть",
        ["Welcome.OpenSettings"] = "Открыть настройки",

        // ===== Settings window: header/footer =====
        ["Settings.Subtitle"] = "Перевод текста с экрана в реальном времени",
        ["Settings.SelectRegionTooltip"] = "Выбрать область экрана для перевода",
        ["Settings.ToggleOnTooltip"] = "Включить перевод",
        ["Settings.ToggleOffTooltip"] = "Выключить перевод",
        ["Settings.Close"] = "Закрыть",
        ["Settings.Save"] = "Сохранить",
        ["Settings.Saved"] = "Сохранено.",

        // ===== Tabs =====
        ["Settings.Tab.Translation"] = "🌍 Перевод",
        ["Settings.Tab.Hotkeys"] = "⌨ Горячие клавиши",
        ["Settings.Tab.Appearance"] = "🎨 Оформление",
        ["Settings.Tab.Glossary"] = "📖 Глоссарий",
        ["Settings.Tab.Other"] = "⚙ Прочее",

        // ===== Tab: Glossary =====
        ["Settings.GlossaryHeader"] = "Имена и термины",
        ["Settings.GlossaryHint"] = "Задайте фиксированный перевод для имён персонажей и других повторяющихся терминов, чтобы они переводились всегда одинаково. Оставьте «Перевод» пустым, чтобы термин вообще не переводился (остался как в оригинале).",
        ["Settings.GlossarySourceColumn"] = "Оригинал",
        ["Settings.GlossaryTargetColumn"] = "Перевод (необязательно)",
        ["Settings.GlossaryAddButton"] = "+ Добавить строку",
        ["Settings.GlossaryProfileDefaultName"] = "Профиль {0}",
        ["Settings.GlossaryProfileNameHint"] = "Название профиля",
        ["Settings.GlossaryProfileAddButton"] = "+ Профиль",
        ["Settings.GlossaryProfileDeleteButton"] = "Удалить профиль",
        ["Settings.GlossaryProfileDeleteLastError"] = "Нельзя удалить единственный профиль.",
        ["Settings.GlossaryExportButton"] = "Экспорт...",
        ["Settings.GlossaryImportButton"] = "Импорт...",
        ["Settings.GlossaryExportError"] = "Не удалось сохранить файл: {0}",
        ["Settings.GlossaryImportError"] = "Не удалось прочитать файл: {0}",
        ["Settings.GlossaryImportEmpty"] = "В файле нет ни одной записи глоссария.",
        ["Settings.GlossaryImported"] = "Импортировано в новый профиль «{0}».",
        ["Settings.GlossaryExported"] = "Профиль «{0}» сохранён.",

        // ===== Tab: Translation =====
        ["Settings.OcrEngineHeader"] = "Способ распознавания текста (OCR)",
        ["Settings.WindowsOcrLink"] = "Открыть языковые настройки Windows",
        ["Settings.SourceLanguageHeader"] = "Язык, который нужно распознавать",
        ["Settings.TargetLanguageHeader"] = "Язык перевода",
        ["Settings.TranslatorHeader"] = "Сервис перевода",
        ["Settings.DeepLHeader"] = "Ключ DeepL",
        ["Settings.DeepLGetKey"] = "Получить бесплатный ключ: ",
        ["Settings.ApiKeyLabel"] = "API-ключ",
        ["Settings.DeepLProCheck"] = "У меня платный ключ Pro (использовать api.deepl.com вместо бесплатного api-free.deepl.com)",
        ["Settings.GoogleHeader"] = "Google Translate",
        ["Settings.GoogleHint"] = "Работает сразу, без ключа (бесплатный способ). Ключ нужен только для гарантированной стабильной работы без ограничений — получить его можно в Google Cloud Console.",
        ["Settings.GoogleGetKey"] = "Получить ключ Cloud Translation API (необязательно)",
        ["Settings.ApiKeyOptionalLabel"] = "API-ключ (необязательно)",
        ["Settings.YandexHeader"] = "Yandex Translate",
        ["Settings.YandexGetKey"] = "Получить ключ и Folder ID: ",
        ["Settings.FolderIdLabel"] = "Folder ID",
        ["Settings.PapagoHeader"] = "Papago (Naver Cloud Platform)",
        ["Settings.PapagoGetKey"] = "Получить Client ID / Secret: ",
        ["Settings.ClientIdLabel"] = "Client ID",
        ["Settings.ClientSecretLabel"] = "Client Secret",
        ["Settings.PapagoHint"] = "Papago поддерживает перевод только между ограниченным набором языков (в основном через корейский/английский/японский/китайский).",
        ["Settings.PollingHeader"] = "Скорость проверки экрана",
        ["Settings.PollingPrefix"] = "Проверять область каждые",
        ["Settings.Seconds"] = "сек",
        ["Settings.SkipUnchangedCheck"] = "Не распознавать и не переводить повторно, если картинка не изменилась",
        ["Settings.BehaviorHeader"] = "Поведение",
        ["Settings.AutoStartCheck"] = "Включать перевод сразу после выбора области захвата",
        ["Settings.LanguageHeader"] = "Язык интерфейса",
        ["Settings.LanguageHint"] = "Изменения полностью применятся после перезапуска программы.",

        // ===== Tab: Hotkeys =====
        ["Settings.HotkeysHeader"] = "Настройка сочетаний клавиш",
        ["Settings.HotkeysHint"] = "Кликните по полю и нажмите нужное сочетание клавиш (обязательно с Ctrl, Alt, Shift или Win). Backspace — очистить, тогда хоткей не назначен.",
        ["Settings.HotkeySelectRegion"] = "Выбрать область экрана",
        ["Settings.HotkeyToggle"] = "Включить / выключить перевод",
        ["Settings.HotkeyOnce"] = "Перевести область один раз (не меняя основную область)",
        ["Settings.HotkeyRetranslate"] = "Перевести заново",
        ["Settings.HoverSectionHeader"] = "Мгновенный перевод",
        ["Settings.HoverSectionHint"] = "Данная функция позволяет переводить весь текст рядом с курсором, не выделяя область. Поддерживаются одиночная клавиша, сочетание клавиш или одиночное нажатие кнопки мыши, кроме ЛКМ. В некоторых играх одиночная клавиша или кнопка мыши может не сработать. В таком случае назначьте сочетание с модификатором (например, Ctrl+F).",
        ["Settings.HoverModeOff"] = "Выкл.",
        ["Settings.HoverModeImmediate"] = "Сразу",
        ["Settings.HoverModeConfirm"] = "После подтверждения",
        ["Settings.HotkeyOpenSettings"] = "Открыть настройки",

        // ===== Tab: Appearance =====
        ["Settings.PositionSizeHeader"] = "Расположение и размер",
        ["Settings.PositionLabel"] = "Положение",
        ["Settings.FontSizeLabel"] = "Размер шрифта",
        ["Settings.MaxWidthLabel"] = "Макс. ширина (px)",
        ["Settings.ColorHeader"] = "Цвет и прозрачность",
        ["Settings.TextColorLabel"] = "Цвет текста (например #FFFFFF)",
        ["Settings.BgColorLabel"] = "Цвет фона (например #000000)",
        ["Settings.PickColorTooltip"] = "Открыть палитру цветов",
        ["Settings.BgOpacityLabel"] = "Прозрачность фона: ",
        ["Settings.BgOpacityHint"] = "0% — фон полностью непрозрачный, 100% — полностью прозрачный.",
        ["Settings.ReadabilityHeader"] = "Читаемость текста",
        ["Settings.ReadabilityHint"] = "Контур и тень — это два разных способа выделить текст на любом фоне; можно выбрать только один.",
        ["Settings.OutlineNone"] = "Нет",
        ["Settings.OutlineOutline"] = "Контур",
        ["Settings.OutlineShadow"] = "Тень",
        ["Settings.WindowBehaviorHeader"] = "Поведение окна",
        ["Settings.ClickThroughCheck"] = "Прозрачное для кликов",
        ["Settings.AutoHideCheck"] = "Автоматически скрывать окно перевода, если текст не обновляется",
        ["Settings.AutoHidePrefix"] = "Скрывать через",

        // ===== Tab: Other =====
        ["Settings.TesseractHeader"] = "Tesseract (для варианта Tesseract)",
        ["Settings.TesseractGetFiles"] = "Скачать файлы языков: ",
        ["Settings.TesseractFolderLabel"] = "Папка с файлами языков (.traineddata)",
        ["Settings.EasyOcrHeader"] = "EasyOCR (для варианта EasyOCR)",
        ["Settings.EasyOcrNeedPython"] = "Нужен Python: ",
        ["Settings.EasyOcrThenRun"] = ", затем команда: pip install easyocr",
        ["Settings.EasyOcrPathLabel"] = "Путь к python.exe",
        ["Settings.EasyOcrGpuCheck"] = "Использовать видеокарту (нужен PyTorch с поддержкой CUDA)",
        ["Settings.StartupHeader"] = "Запуск",
        ["Settings.RunAtStartupCheck"] = "Запускать KainoTranslator вместе с Windows",
        ["Settings.ProxyHeader"] = "Список прокси",
        ["Settings.ProxyHint"] = "По одному прокси на строку, например: http://1.2.3.4:8080 или http://логин:пароль@1.2.3.4:8080. Помогает избежать блокировок при частых запросах (в основном для бесплатного Google). Оставьте пустым, если прокси не нужны.",
        ["Settings.RotateProxyCheck"] = "Чередовать прокси при каждом запросе (а не использовать только первый)",

        // ===== Region selector =====
        ["Region.Title"] = "KainoTranslator - выбор области",
        ["Region.Hint"] = "Выделите область с текстом для перевода  •  Esc — отмена",
        ["HoverConfirm.Hint"] = "Кликните по тексту для перевода  •  Esc — отмена",

        // ===== OCR/Translator hints (dynamic, built in code) =====
        ["Ocr.WindowsOcr"] = "Встроен в Windows — быстро и без установки. Но для выбранного языка должен быть установлен компонент распознавания текста (ссылка ниже).",
        ["Ocr.Tesseract"] = "Работает офлайн. Нужны файлы .traineddata для нужных языков (папка указывается на вкладке «Прочее»).",
        ["Ocr.EasyOcr"] = "Нужен Python и библиотека easyocr (см. вкладку «Прочее»). Медленнее из-за загрузки модели.",
        ["Translator.DeepL"] = "Нужен API-ключ (есть бесплатный тариф) — поле ниже.",
        ["Translator.Yandex"] = "Нужны API-ключ и Folder ID из Yandex Cloud — поля ниже.",
        ["Translator.Papago"] = "Нужны Client ID и Secret из NAVER Cloud Platform — поля ниже. Ограниченный набор языков.",
        ["Translator.Google"] = "Работает сразу без ключа (бесплатный способ).",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        // ===== Tray menu =====
        ["Tray.SelectRegion"] = "Select capture area",
        ["Tray.ToggleTranslation"] = "Start / stop translation",
        ["Tray.TranslateOnce"] = "Translate an area once",
        ["Tray.HoverTranslate"] = "Instant translate",
        ["Tray.Settings"] = "Settings...",
        ["Tray.Help"] = "Help / getting started",
        ["Tray.About"] = "About",
        ["Tray.Exit"] = "Exit",

        // ===== Balloons / tray notifications =====
        ["Balloon.Started"] = "The app has started and is running in the tray.",
        ["Balloon.StillRunning"] = "The app is still running in the tray.",
        ["Balloon.HotkeyConflictTitle"] = "Hotkey conflict",
        ["Balloon.HotkeyConflict.SelectRegion"] = "Couldn't register '{0}' for selecting the capture area - another app may already be using it.",
        ["Balloon.HotkeyConflict.Toggle"] = "Couldn't register '{0}' for starting/stopping translation - another app may already be using it.",
        ["Balloon.HotkeyConflict.Settings"] = "Couldn't register '{0}' for opening settings - another app may already be using it.",
        ["Balloon.HotkeyConflict.OneTime"] = "Couldn't register '{0}' for one-time translation - another app may already be using it.",
        ["Balloon.HotkeyConflict.Retranslate"] = "Couldn't register '{0}' for re-translating - another app may already be using it.",
        ["Balloon.HotkeyConflict.HoverCombo"] = "Couldn't register '{0}' for instant translate - another app may already be using it.",
        ["Balloon.RegionSetTitle"] = "Capture area set",
        ["Balloon.RegionSetText"] = "{0}×{1} at ({2},{3}).",
        ["Balloon.OneTimeNoText"] = "Couldn't recognize any text in the selected area.",
        ["Balloon.OneTimeError"] = "One-time translation error: {0}",
        ["Balloon.RetranslateError"] = "Re-translation error: {0}",
        ["Balloon.HoverKeyInvalid"] = "Couldn't enable hover-translate: invalid trigger key in settings.",
        ["Warning.NoRegionText"] = "First select a capture area for translation (the 'Select capture area' hotkey), then turn translation on.",

        // ===== About window =====
        ["About.Ocr"] = "Text recognition:",
        ["About.OcrValue"] = "Windows OCR, Tesseract, EasyOCR",
        ["About.Translation"] = "Translation:",
        ["About.TranslationValue"] = "DeepL, Google Translate, Yandex Translate, Papago",
        ["About.Close"] = "Close",

        // ===== Welcome window =====
        ["Welcome.Subtitle"] = "Live translation of on-screen text - for games, subtitles, and more.",
        ["Welcome.TrayHeader"] = "The app runs from the system tray",
        ["Welcome.TrayBody"] = "There's no separate window - the app lives as an icon next to the clock (bottom-right, possibly under the \"Show hidden icons\" arrow). Right-click it any time to open the menu.",
        ["Welcome.HotkeysHeader"] = "Hotkeys",
        ["Welcome.HotkeySelectRegion"] = "Select capture area",
        ["Welcome.HotkeyToggle"] = "Start / stop translation",
        ["Welcome.HotkeyOnce"] = "Translate an area once (without changing the main area)",
        ["Welcome.HotkeyOpenSettings"] = "Open settings",
        ["Welcome.HotkeysFooter"] = "You can change these in Settings. They work even when a game is fullscreen.",
        ["Welcome.GameTipHeader"] = "Tip for games",
        ["Welcome.GameTipBody"] = "Run the game in \"Borderless\" (windowed fullscreen) mode - that way the overlay is guaranteed to show above the game. In true \"Fullscreen\" mode the overlay may sometimes not show up. You can usually switch modes with Alt+Enter or in the game's display settings.",
        ["Welcome.DefaultsHeader"] = "What should I pick by default?",
        ["Welcome.DefaultsBody1"] = "Recognition - ",
        ["Welcome.DefaultsBody1Bold"] = "Windows OCR",
        ["Welcome.DefaultsBody2"] = " (already on, works out of the box). Translation - ",
        ["Welcome.DefaultsBody2Bold"] = "Google Translate",
        ["Welcome.DefaultsBody3"] = " (also works immediately, no key needed). You can change any of this in Settings at any time.",
        ["Welcome.DontShowAgain"] = "Don't show this window again",
        ["Welcome.Close"] = "Close",
        ["Welcome.OpenSettings"] = "Open settings",

        // ===== Settings window: header/footer =====
        ["Settings.Subtitle"] = "Live translation of on-screen text",
        ["Settings.SelectRegionTooltip"] = "Select the screen area to translate",
        ["Settings.ToggleOnTooltip"] = "Start translation",
        ["Settings.ToggleOffTooltip"] = "Stop translation",
        ["Settings.Close"] = "Close",
        ["Settings.Save"] = "Save",
        ["Settings.Saved"] = "Saved.",

        // ===== Tabs =====
        ["Settings.Tab.Translation"] = "🌍 Translation",
        ["Settings.Tab.Hotkeys"] = "⌨ Hotkeys",
        ["Settings.Tab.Appearance"] = "🎨 Appearance",
        ["Settings.Tab.Glossary"] = "📖 Glossary",
        ["Settings.Tab.Other"] = "⚙ Other",

        // ===== Tab: Glossary =====
        ["Settings.GlossaryHeader"] = "Names and terms",
        ["Settings.GlossaryHint"] = "Set a fixed translation for character names and other recurring terms so they're always translated the same way. Leave \"Translation\" empty to keep the term untranslated (exactly as in the original).",
        ["Settings.GlossarySourceColumn"] = "Original",
        ["Settings.GlossaryTargetColumn"] = "Translation (optional)",
        ["Settings.GlossaryAddButton"] = "+ Add row",
        ["Settings.GlossaryProfileDefaultName"] = "Profile {0}",
        ["Settings.GlossaryProfileNameHint"] = "Profile name",
        ["Settings.GlossaryProfileAddButton"] = "+ Profile",
        ["Settings.GlossaryProfileDeleteButton"] = "Delete profile",
        ["Settings.GlossaryProfileDeleteLastError"] = "Can't delete the only profile.",
        ["Settings.GlossaryExportButton"] = "Export...",
        ["Settings.GlossaryImportButton"] = "Import...",
        ["Settings.GlossaryExportError"] = "Couldn't save the file: {0}",
        ["Settings.GlossaryImportError"] = "Couldn't read the file: {0}",
        ["Settings.GlossaryImportEmpty"] = "The file has no glossary entries.",
        ["Settings.GlossaryImported"] = "Imported into a new profile, \"{0}\".",
        ["Settings.GlossaryExported"] = "Profile \"{0}\" saved.",

        // ===== Tab: Translation =====
        ["Settings.OcrEngineHeader"] = "Text recognition method (OCR)",
        ["Settings.WindowsOcrLink"] = "Open Windows language settings",
        ["Settings.SourceLanguageHeader"] = "Language to recognize",
        ["Settings.TargetLanguageHeader"] = "Translation language",
        ["Settings.TranslatorHeader"] = "Translation service",
        ["Settings.DeepLHeader"] = "DeepL key",
        ["Settings.DeepLGetKey"] = "Get a free key: ",
        ["Settings.ApiKeyLabel"] = "API key",
        ["Settings.DeepLProCheck"] = "I have a paid Pro key (use api.deepl.com instead of the free api-free.deepl.com)",
        ["Settings.GoogleHeader"] = "Google Translate",
        ["Settings.GoogleHint"] = "Works right away, no key needed (free method). A key is only needed for guaranteed uptime without limits - get one in the Google Cloud Console.",
        ["Settings.GoogleGetKey"] = "Get a Cloud Translation API key (optional)",
        ["Settings.ApiKeyOptionalLabel"] = "API key (optional)",
        ["Settings.YandexHeader"] = "Yandex Translate",
        ["Settings.YandexGetKey"] = "Get a key and Folder ID: ",
        ["Settings.FolderIdLabel"] = "Folder ID",
        ["Settings.PapagoHeader"] = "Papago (Naver Cloud Platform)",
        ["Settings.PapagoGetKey"] = "Get a Client ID / Secret: ",
        ["Settings.ClientIdLabel"] = "Client ID",
        ["Settings.ClientSecretLabel"] = "Client Secret",
        ["Settings.PapagoHint"] = "Papago only supports translation between a limited set of languages (mostly via Korean/English/Japanese/Chinese).",
        ["Settings.PollingHeader"] = "Screen check speed",
        ["Settings.PollingPrefix"] = "Check the area every",
        ["Settings.Seconds"] = "sec",
        ["Settings.SkipUnchangedCheck"] = "Don't re-recognize or re-translate if the picture hasn't changed",
        ["Settings.BehaviorHeader"] = "Behavior",
        ["Settings.AutoStartCheck"] = "Start translation right after selecting the capture area",
        ["Settings.LanguageHeader"] = "Interface language",
        ["Settings.LanguageHint"] = "Changes take full effect after restarting the app.",

        // ===== Tab: Hotkeys =====
        ["Settings.HotkeysHeader"] = "Hotkey configuration",
        ["Settings.HotkeysHint"] = "Click a field and press the combination you want (must include Ctrl, Alt, Shift, or Win). Backspace clears it, leaving that hotkey unset.",
        ["Settings.HotkeySelectRegion"] = "Select capture area",
        ["Settings.HotkeyToggle"] = "Start / stop translation",
        ["Settings.HotkeyOnce"] = "Translate an area once (without changing the main area)",
        ["Settings.HotkeyRetranslate"] = "Re-translate",
        ["Settings.HoverSectionHeader"] = "Instant translate",
        ["Settings.HoverSectionHint"] = "This feature translates all the text near the cursor without selecting a region. It supports a single key, a keyboard combo, or a single mouse button press (except left-click). In some games a single key or mouse button may not work. In that case, bind a modifier combo instead (e.g. Ctrl+F).",
        ["Settings.HoverModeOff"] = "Off",
        ["Settings.HoverModeImmediate"] = "Immediately",
        ["Settings.HoverModeConfirm"] = "After confirming",
        ["Settings.HotkeyOpenSettings"] = "Open settings",

        // ===== Tab: Appearance =====
        ["Settings.PositionSizeHeader"] = "Position and size",
        ["Settings.PositionLabel"] = "Position",
        ["Settings.FontSizeLabel"] = "Font size",
        ["Settings.MaxWidthLabel"] = "Max width (px)",
        ["Settings.ColorHeader"] = "Color and transparency",
        ["Settings.TextColorLabel"] = "Text color (e.g. #FFFFFF)",
        ["Settings.BgColorLabel"] = "Background color (e.g. #000000)",
        ["Settings.PickColorTooltip"] = "Open the color picker",
        ["Settings.BgOpacityLabel"] = "Background transparency: ",
        ["Settings.BgOpacityHint"] = "0% - background fully opaque, 100% - fully transparent.",
        ["Settings.ReadabilityHeader"] = "Text readability",
        ["Settings.ReadabilityHint"] = "Outline and shadow are two different ways to make text stand out on any background; only one can be used at a time.",
        ["Settings.OutlineNone"] = "None",
        ["Settings.OutlineOutline"] = "Outline",
        ["Settings.OutlineShadow"] = "Shadow",
        ["Settings.WindowBehaviorHeader"] = "Window behavior",
        ["Settings.ClickThroughCheck"] = "Click-through",
        ["Settings.AutoHideCheck"] = "Automatically hide the overlay if the text stops updating",
        ["Settings.AutoHidePrefix"] = "Hide after",

        // ===== Tab: Other =====
        ["Settings.TesseractHeader"] = "Tesseract (for the Tesseract option)",
        ["Settings.TesseractGetFiles"] = "Download language files: ",
        ["Settings.TesseractFolderLabel"] = "Folder with language files (.traineddata)",
        ["Settings.EasyOcrHeader"] = "EasyOCR (for the EasyOCR option)",
        ["Settings.EasyOcrNeedPython"] = "Requires Python: ",
        ["Settings.EasyOcrThenRun"] = ", then run: pip install easyocr",
        ["Settings.EasyOcrPathLabel"] = "Path to python.exe",
        ["Settings.EasyOcrGpuCheck"] = "Use GPU (requires a CUDA-enabled PyTorch build)",
        ["Settings.StartupHeader"] = "Startup",
        ["Settings.RunAtStartupCheck"] = "Start KainoTranslator with Windows",
        ["Settings.ProxyHeader"] = "Proxy list",
        ["Settings.ProxyHint"] = "One proxy per line, e.g.: http://1.2.3.4:8080 or http://user:pass@1.2.3.4:8080. Helps avoid blocks under heavy use (mainly for the free Google option). Leave empty if you don't need proxies.",
        ["Settings.RotateProxyCheck"] = "Rotate proxies on every request (instead of always using the first one)",

        // ===== Region selector =====
        ["Region.Title"] = "KainoTranslator - select area",
        ["Region.Hint"] = "Drag to select the text area to translate  •  Esc to cancel",
        ["HoverConfirm.Hint"] = "Click the text to translate  •  Esc to cancel",

        // ===== OCR/Translator hints (dynamic, built in code) =====
        ["Ocr.WindowsOcr"] = "Built into Windows - fast, no install needed. The chosen language's text-recognition component must be installed (link below).",
        ["Ocr.Tesseract"] = "Works offline. Needs .traineddata files for the languages you use (folder set on the \"Other\" tab).",
        ["Ocr.EasyOcr"] = "Needs Python and the easyocr library (see the \"Other\" tab). Slower due to model loading.",
        ["Translator.DeepL"] = "Needs an API key (free tier available) - field below.",
        ["Translator.Yandex"] = "Needs an API key and Folder ID from Yandex Cloud - fields below.",
        ["Translator.Papago"] = "Needs a Client ID and Secret from NAVER Cloud Platform - fields below. Limited language set.",
        ["Translator.Google"] = "Works right away, no key needed (free method).",
    };
}
