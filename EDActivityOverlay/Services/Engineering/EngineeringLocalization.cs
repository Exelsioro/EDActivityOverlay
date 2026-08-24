using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Engineering;

/// <summary>
/// Russian names used by the Russian Elite Dangerous client. Keys are journal/catalog identifiers.
/// The fallback table is adapted from EDDiscovery/EliteDangerousCore translation-russian-ed.tlp
/// (Apache-2.0); journal-provided localized names always take precedence.
/// </summary>
public static class EngineeringLocalization
{
    public sealed record CategoryFilter(string Label, EngineeringMaterialCategory? Category);

    public static IReadOnlyList<CategoryFilter> CategoryFilters =>
    [
        new(Loc.Get("Loc_All_categories"), null),
        new(Loc.Get("Loc_Raw"), EngineeringMaterialCategory.Raw),
        new(Loc.Get("Loc_Manufactured"), EngineeringMaterialCategory.Manufactured),
        new(Loc.Get("Loc_Encoded"), EngineeringMaterialCategory.Encoded),
        new(Loc.Get("Loc_Odyssey_Items"), EngineeringMaterialCategory.Item),
        new(Loc.Get("Loc_Odyssey_Components"), EngineeringMaterialCategory.Component),
        new(Loc.Get("Loc_Odyssey_Data"), EngineeringMaterialCategory.Data),
        new(Loc.Get("Loc_Consumables"), EngineeringMaterialCategory.Consumable)
    ];

    private static readonly IReadOnlyDictionary<string, string> RussianNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["carbon"] = "Углерод", ["iron"] = "Железо", ["nickel"] = "Никель",
            ["phosphorus"] = "Фосфор", ["sulphur"] = "Сера", ["lead"] = "Свинец",
            ["rhenium"] = "Рений", ["chromium"] = "Хром", ["germanium"] = "Германий",
            ["manganese"] = "Марганец", ["vanadium"] = "Ванадий", ["zinc"] = "Цинк",
            ["zirconium"] = "Цирконий", ["arsenic"] = "Мышьяк", ["niobium"] = "Ниобий",
            ["tungsten"] = "Вольфрам", ["molybdenum"] = "Молибден", ["mercury"] = "Ртуть",
            ["boron"] = "Бор", ["cadmium"] = "Кадмий", ["tin"] = "Олово",
            ["selenium"] = "Селен", ["yttrium"] = "Иттрий", ["technetium"] = "Технеций",
            ["tellurium"] = "Теллур", ["ruthenium"] = "Рутений", ["polonium"] = "Полоний",
            ["antimony"] = "Сурьма",

            ["anomalousbulkscandata"] = "Аномальный массив данных сканирования",
            ["atypicaldisruptedwakeechoes"] = "Атипичное эхо поврежденного следа",
            ["distortedshieldcyclerecordings"] = "Поврежденные цикличные записи щита",
            ["exceptionalscrambledemissiondata"] = "Исключительные зашифрованные данные об излучении",
            ["specialisedlegacyfirmware"] = "Специальные микропрограммы предыдущего поколения",
            ["unusualencryptedfiles"] = "Особые зашифрованные файлы",
            ["anomalousfsdtelemetry"] = "Аномальная телеметрия FSD",
            ["inconsistentshieldsoakanalysis"] = "Неполный анализ поглощения щита",
            ["irregularemissiondata"] = "Нестандартные данные об излучении",
            ["modifiedconsumerfirmware"] = "Измененные пользовательские микропрограммы",
            ["taggedencryptioncodes"] = "Меченые шифровальные коды",
            ["unidentifiedscanarchives"] = "Неопознанные архивы сканирования",
            ["patternbetaobeliskdata"] = "Данные с обелиска «Бета»",
            ["patterngammaobeliskdata"] = "Данные с обелиска «Гамма»",
            ["classifiedscandatabanks"] = "Засекреченные базы данных сканирования",
            ["crackedindustrialfirmware"] = "Взломанные промышленные микропрограммы",
            ["opensymmetrickeys"] = "Открытые симметричные ключи",
            ["strangewakesolutions"] = "Странные расчеты следа",
            ["unexpectedemissiondata"] = "Неожиданные данные об излучении",
            ["untypicalshieldscans"] = "Нетипичные данные сканирования щитов",
            ["abnormalcompactemissionsdata"] = "Аномальные компактные данные об излучении",
            ["patternalphaobeliskdata"] = "Данные с обелиска «Альфа»",
            ["aberrantshieldpatternanalysis"] = "Анализ аномального поведения щита",
            ["atypicalencryptionarchives"] = "Нетипичные архивы шифрования",
            ["decodedemissiondata"] = "Расшифрованные данные об излучении",
            ["divergentscandata"] = "Неформатные данные сканирования",
            ["eccentrichyperspacetrajectories"] = "Аномальные траектории в гиперпространстве",
            ["securityfirmwarepatch"] = "Обновление для защитной микропрограммы",
            ["patterndeltaobeliskdata"] = "Данные с обелиска «Дельта»",
            ["classifiedscanfragment"] = "Засекреченные фрагменты данных сканирования",
            ["modifiedembeddedfirmware"] = "Измененные встроенные микропрограммы",
            ["adaptiveencryptorscapture"] = "Захват адаптивного шифровальщика",
            ["dataminedwakeexceptions"] = "Исключения из глубинного анализа данных следа",
            ["peculiarshieldfrequencydata"] = "Специфические данные о частоте щитов",
            ["patternepsilonobeliskdata"] = "Данные с обелиска «Эпсилон»",

            ["basicconductors"] = "Простые проводники", ["chemicalstorageunits"] = "Контейнеры для химикатов",
            ["compactcomposites"] = "Спрессованные композиты", ["crystalshards"] = "Осколки кристаллов",
            ["gridresistors"] = "Наборные резисторы", ["heatconductionwiring"] = "Теплопроводящие провода",
            ["mechanicalscrap"] = "Механические отходы", ["salvagedalloys"] = "Захваченные сплавы",
            ["wornshieldemitters"] = "Изношенные щитоизлучатели", ["temperedalloys"] = "Закаленные сплавы",
            ["chemicalprocessors"] = "Оборудование для химобработки", ["conductivecomponents"] = "Проводящие компоненты",
            ["filamentcomposites"] = "Волокнистые композиты", ["flawedfocuscrystals"] = "Поврежденные фокусировочные кристаллы",
            ["galvanisingalloys"] = "Сплавы для гальванизации", ["heatdispersionplate"] = "Теплорассеивающая пластина",
            ["heatresistantceramics"] = "Жаропрочная керамика", ["hybridcapacitors"] = "Гибридные конденсаторы",
            ["mechanicalequipment"] = "Механическое оборудование", ["shieldemitters"] = "Щитоизлучатели",
            ["chemicaldistillery"] = "Оборудование для перегонки химикатов", ["conductiveceramics"] = "Проводящая керамика",
            ["electrochemicalarrays"] = "Электрохимические массивы", ["focuscrystals"] = "Фокусировочные кристаллы",
            ["heatexchangers"] = "Теплообменные агрегаты", ["highdensitycomposites"] = "Высокоплотностные композиты",
            ["mechanicalcomponents"] = "Механические компоненты", ["phasealloys"] = "Фазовые сплавы",
            ["precipitatedalloys"] = "Осажденные сплавы", ["shieldingsensors"] = "Сенсоры системы экранирования",
            ["guardianpowercell"] = "Энергоячейка защитника", ["guardianpowerconduit"] = "Энергопроводники защитника",
            ["guardiantechnologycomponent"] = "Компоненты технологий защитника",
            ["guardiansentinelweaponparts"] = "Детали вооружения защитника Sentinel",
            ["guardianwreckagecomponents"] = "Обломки кораблекрушений защитника Sentinel",
            ["guardianweaponblueprintfragment"] = "Фрагмент чертежа оружия защитника",
            ["guardianmoduleblueprintfragment"] = "Фрагмент чертежа модуля защитника",
            ["biomechanicalconduits"] = "Биомеханические энергопроводники", ["propulsionelements"] = "Реактивные элементы",
            ["weaponparts"] = "Детали вооружения", ["wreckagecomponents"] = "Обломки кораблекрушений",
            ["shipflightdata"] = "Полетные данные корабля", ["shipsystemsdata"] = "Данные бортовых систем корабля",
            ["chemicalmanipulators"] = "Манипуляторы для работы с химикатами", ["compoundshielding"] = "Многоступенчатая защита",
            ["conductivepolymers"] = "Проводящие полимеры", ["configurablecomponents"] = "Настраиваемые компоненты",
            ["heatvanes"] = "Тепловые заслонки", ["polymercapacitors"] = "Полимерные конденсаторы",
            ["protolightalloys"] = "Опытные легкие сплавы", ["refinedfocuscrystals"] = "Обработанные фокусировочные кристаллы",
            ["proprietarycomposites"] = "Патентованные композиты", ["thermicalloys"] = "Термические сплавы",
            ["coredynamicscomposites"] = "Композиты Core Dynamics", ["biotechconductors"] = "Биотехнические проводники",
            ["exquisitefocuscrystals"] = "Отборные фокусировочные кристаллы", ["imperialshielding"] = "Имперская защита",
            ["improvisedcomponents"] = "Кустарные компоненты", ["militarygradealloys"] = "Сплавы военного назначения",
            ["militarysupercapacitors"] = "Военные суперконденсаторы",
            ["pharmaceuticalisolators"] = "Фармацевтические изоляционные материалы",
            ["protoheatradiators"] = "Прототипы теплоизлучателей", ["protoradiolicalloys"] = "Сплавы для изготовления зондов"
        };

    public static string MaterialName(string materialId, string fallback)
    {
        if (!LocalizationService.Instance.CurrentLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }
        if (fallback.Any(character => character is >= 'А' and <= 'я' or 'Ё' or 'ё')) return fallback;
        string id = global::EDActivityOverlay.Services.Engineering.MaterialName.Normalize(materialId);
        if (RussianNames.TryGetValue(id, out string? translated)) return translated;
        id = global::EDActivityOverlay.Services.Engineering.MaterialName.Normalize(fallback);
        return RussianNames.TryGetValue(id, out translated) ? translated : fallback;
    }

    public static string CategoryName(EngineeringMaterialCategory category) => category switch
    {
        EngineeringMaterialCategory.Raw => Loc.Get("Loc_Raw"),
        EngineeringMaterialCategory.Manufactured => Loc.Get("Loc_Manufactured"),
        EngineeringMaterialCategory.Encoded => Loc.Get("Loc_Encoded"),
        EngineeringMaterialCategory.Item => Loc.Get("Loc_Odyssey_Items"),
        EngineeringMaterialCategory.Component => Loc.Get("Loc_Odyssey_Components"),
        EngineeringMaterialCategory.Data => Loc.Get("Loc_Odyssey_Data"),
        EngineeringMaterialCategory.Consumable => Loc.Get("Loc_Consumables"),
        _ => Loc.Get("Loc_Unknown")
    };
}
