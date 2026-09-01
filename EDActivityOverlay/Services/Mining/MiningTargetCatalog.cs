using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningTargetOption(
    string CommodityId,
    string EnglishName,
    string RussianName)
{
    public string Label => MiningTargetCatalog.GetDisplayName(this);
}

public static class MiningTargetCatalog
{
    private static readonly MiningTargetOption NoTarget =
        new(string.Empty, "— NO TARGET —", "— НЕТ ЦЕЛИ —");

    private static readonly MiningTargetOption[] Mineable =
    [
        new("Opal", "Void Opals", "Пустотные опалы"),
        new("LowTemperatureDiamond", "Low Temperature Diamonds", "Низкотемпературные алмазы"),
        new("Alexandrite", "Alexandrite", "Александрит"),
        new("Grandidierite", "Grandidierite", "Грандидьерит"),
        new("Musgravite", "Musgravite", "Мусгравит"),
        new("Monazite", "Monazite", "Монацит"),
        new("Serendibite", "Serendibite", "Серендибит"),
        new("Rhodplumsite", "Rhodplumsite", "Родплумсит"),
        new("Benitoite", "Benitoite", "Бенитоит"),
        new("Painite", "Painite", "Пейнит"),
        new("Bromellite", "Bromellite", "Бромеллит"),
        new("LithiumHydroxide", "Lithium Hydroxide", "Гидроксид лития"),
        new("Bertrandite", "Bertrandite", "Бертрандит"),
        new("MethanolMonohydrateCrystals", "Methanol Monohydrate Crystals", "Кристаллы моногидрата метанола"),
        new("Indite", "Indite", "Индит"),
        new("Gallite", "Gallite", "Галлит"),
        new("Coltan", "Coltan", "Колтан"),
        new("Uraninite", "Uraninite", "Уранинит"),
        new("MethaneClathrate", "Methane Clathrate", "Клатрат метана"),
        new("Lepidolite", "Lepidolite", "Лепидолит"),
        new("Rutile", "Rutile", "Рутил"),
        new("Bauxite", "Bauxite", "Боксит"),
        new("Platinum", "Platinum", "Платина"),
        new("Palladium", "Palladium", "Палладий"),
        new("Thorium", "Thorium", "Торий"),
        new("Gold", "Gold", "Золото"),
        new("Osmium", "Osmium", "Осмий"),
        new("Praseodymium", "Praseodymium", "Празеодим"),
        new("Samarium", "Samarium", "Самарий"),
        new("Silver", "Silver", "Серебро"),
        new("Cobalt", "Cobalt", "Кобальт"),
        new("Tritium", "Tritium", "Тритий"),
        new("HydrogenPeroxide", "Hydrogen Peroxide", "Пероксид водорода"),
        new("LiquidOxygen", "Liquid Oxygen", "Жидкий кислород"),
        new("Water", "Water", "Вода")
    ];

    public static IReadOnlyList<MiningTargetOption> Targets => Mineable;

    public static IReadOnlyList<MiningTargetOption> GetLocalizedOptions()
    {
        return new[] { NoTarget }
            .Concat(
                Mineable.OrderBy(
                    item => GetDisplayName(item),
                    StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
    }

    public static MiningTargetOption? Find(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NoTarget;
        }

        string normalized = CommodityIdentity.Normalize(value);
        return Mineable.FirstOrDefault(item =>
            CommodityIdentity.Normalize(item.CommodityId)
                .Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || item.EnglishName.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)
            || item.RussianName.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string GetDisplayName(string? commodityId)
    {
        MiningTargetOption? option = Find(commodityId);
        return option is null
            ? commodityId?.Trim() ?? string.Empty
            : GetDisplayName(option);
    }

    public static string GetDisplayName(
        MiningTargetOption option,
        string? language = null)
    {
        ArgumentNullException.ThrowIfNull(option);

        string current =
            language
            ?? LocalizationService.Instance.CurrentLanguage;

        return current.StartsWith(
            "ru",
            StringComparison.OrdinalIgnoreCase)
            ? option.RussianName
            : option.EnglishName;
    }
}
