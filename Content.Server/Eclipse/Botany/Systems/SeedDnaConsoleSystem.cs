using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Eclipse.Botany.SeedDna;
using Content.Shared.Eclipse.Botany.SeedDna.Components;
using Content.Shared.Eclipse.Botany.SeedDna.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.Eclipse.Botany.Systems;

public sealed class SeedDnaConsoleSystem : SharedSeedDnaConsoleSystem
{
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SeedDnaConsoleComponent, ComponentStartup>(UpdateReport);
        SubscribeLocalEvent<SeedDnaConsoleComponent, EntInsertedIntoContainerMessage>(UpdateReport);
        SubscribeLocalEvent<SeedDnaConsoleComponent, EntRemovedFromContainerMessage>(UpdateReport);
        // Legacy disks can be ejected, but neither disks nor seeds can be rewritten.
    }

    private void UpdateReport(EntityUid uid, SeedDnaConsoleComponent comp, EntityEventArgs args)
    {
        if (!comp.Initialized)
            return;
        var report = new List<string>();
        var name = string.Empty;
        if (comp.SeedSlot.Item is { Valid: true } item)
        {
            name = MetaData(item).EntityName;
            if (TryComp<SeedComponent>(item, out var seedComp) && _botany.TryGetSeed(seedComp, out var seed))
                DescribeSeed(seed, report);
            else
                report.Add(Loc.GetString("seed-dna-window-invalid-seed"));
        }
        _ui.SetUiState(uid, SeedDnaConsoleUiKey.Key,
            new SeedDnaConsoleBoundUserInterfaceState(comp.SeedSlot.Item != null, name, comp.DnaDiskSlot.Item != null, report));
    }

    private void DescribeSeed(SeedData seed, List<string> report)
    {
        Add("Yield", seed.Yield, 2, 5);
        Add("Potency", seed.Potency, 15, 45);
        Add("Endurance", seed.Endurance, 50, 100);
        Add("Lifespan", seed.Lifespan, 20, 50);
        Add("Maturation", seed.Maturation, 5, 10);
        Add("Production", seed.Production, 4, 8);
        Add("WaterConsumption", seed.WaterConsumption, 0.4f, 0.8f);
        Add("NutrientConsumption", seed.NutrientConsumption, 0.4f, 0.8f);
        Add("PestTolerance", seed.PestTolerance, 3, 7);
        Add("WeedTolerance", seed.WeedTolerance, 3, 7);
        Add("HeatTolerance", seed.HeatTolerance, 5, 20);
        Add("ToxinsTolerance", seed.ToxinsTolerance, 3, 10);
        var climate = seed.IdealHeat < 285 ? "cool" : seed.IdealHeat <= 303 ? "room" : "warm";
        report.Add($"{Loc.GetString("seed-dna-row-IdealHeat")}: {Loc.GetString($"seed-dna-climate-{climate}")}");
        report.Add($"{Loc.GetString("seed-dna-row-HarvestRepeat")}: {Loc.GetString($"seed-dna-harvest-{seed.HarvestRepeat.ToString().ToLowerInvariant()}")}");
        report.Add(Loc.GetString(seed.Seedless ? "seed-dna-sterile" : "seed-dna-fertile"));
        if (seed.PowerProduction > 0)
            report.Add(Loc.GetString("seed-dna-bioelectric"));
        foreach (var (id, chem) in seed.Chemicals)
        {
            if (!_prototypes.TryIndex<ReagentPrototype>(id, out var reagent))
                continue;
            var amount = chem.PotencyDivisor > 0
                ? Math.Min(chem.Max.Float(), chem.Min.Float() + seed.Potency / chem.PotencyDivisor)
                : chem.Max.Float();
            report.Add($"{reagent.LocalizedName}: {Loc.GetString(amount < 3 ? "seed-dna-concentration-low" : amount < 8 ? "seed-dna-concentration-medium" : "seed-dna-concentration-high")}");
        }
        void Add(string trait, float value, float low, float high)
        {
            var band = value < low ? "low" : value < high ? "medium" : "high";
            var category = trait is "Maturation" or "Production" or "Lifespan" ? "duration" : "rating";
            report.Add($"{Loc.GetString($"seed-dna-row-{trait}")}: {Loc.GetString($"seed-dna-{category}-{band}")}");
        }
    }
}
