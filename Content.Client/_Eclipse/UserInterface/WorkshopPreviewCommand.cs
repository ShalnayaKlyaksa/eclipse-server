#if DEBUG
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Content.Client._Eclipse.Beekeeping;
using Content.Client._Eclipse.Cooking;
using Content.Shared._Eclipse;
using Content.Shared._Eclipse.Beekeeping;
using Content.Shared._Eclipse.Cooking;
using Robust.Client;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Client.Graphics;
using Robust.Shared.Asynchronous;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;

namespace Content.Client._Eclipse.UserInterface;

/// <summary>Local, read-only UI review without connecting to or modifying a running round.</summary>
public sealed class WorkshopPreviewCommand : IConsoleCommand
{
    public string Command => "workshop-preview";
    public string Description => "Preview the apiary or kitchen UI using sample data (DEBUG only).";
    public string Help => "workshop-preview apiary|selection|cooking|oven|pan|bowl|steam [screenshot-name.png]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var tasks = IoCManager.Resolve<ITaskManager>();
        var client = IoCManager.Resolve<IBaseClient>();
        await Task.Delay(1500);
        var initialized = new TaskCompletionSource();
        tasks.RunOnMainThread(() =>
        {
            if (client.RunLevel == ClientRunLevel.Initialize) client.StartSinglePlayer();
            initialized.SetResult();
        });
        await initialized.Task;
        await Task.Delay(500);
        tasks.RunOnMainThread(() => Open(args));
    }

    private static async void Export(string[] args)
    {
        if (args.Length < 2) return;
        var path = new ResPath("/Screenshots/Workshop") / args[1];
        var tasks = IoCManager.Resolve<ITaskManager>();
        var clyde = IoCManager.Resolve<IClyde>();
        var resources = IoCManager.Resolve<IResourceManager>();
        resources.UserData.CreateDir(new ResPath("/Screenshots/Workshop"));
        await Task.Delay(1500);
        tasks.RunOnMainThread(() => clyde.Screenshot(ScreenshotType.Final, image =>
        {
            using var file = resources.UserData.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            image.SaveAsPng(file);
            image.Dispose();
        }));
    }

    private static void Open(string[] args)
    {
        if (args.FirstOrDefault() is "cooking" or "oven" or "pan" or "bowl" or "steam")
        {
            var (prototype, method, methods) = args[0] switch
            {
                "oven" => ("EclipseCulinaryOven", CulinaryMethod.Bake, new[] { CulinaryMethod.Bake }),
                "pan" => ("EclipseCulinaryGriddle", CulinaryMethod.Fry, new[] { CulinaryMethod.Fry }),
                "bowl" => ("EclipseCulinaryMixingBowl", CulinaryMethod.Soak, new[] { CulinaryMethod.Soak, CulinaryMethod.Marinate }),
                "steam" => ("EclipseCulinarySteamer", CulinaryMethod.Steam, new[] { CulinaryMethod.Steam }),
                _ => ("EclipseCulinaryStation", CulinaryMethod.Simmer, new[] { CulinaryMethod.Boil, CulinaryMethod.Simmer, CulinaryMethod.Reduce }),
            };
            var window = new CulinaryWindow();
            var working = args[0] == "cooking";
            window.Apply(new CulinaryState(["мясо", "картофель", "лук", "грибы"], "", 120, method, working ? .45f : 0, working, false, true,
                working ? "culinary-status-working" : "culinary-status-idle")
            {
                StationName = IoCManager.Resolve<IPrototypeManager>().Index<EntityPrototype>(prototype).Name,
                AllowedMethods = methods,
                InputSlots = [new(0, "FoodMeat", "Мясо"), new(1, "FoodPotato", "Картофель"), new(2, "FoodOnion", "Лук"), new(3, "FoodMushroom", "Грибы")],
                PropertyValues = [new(CulinaryProperty.Recovery, 4.2f), new(CulinaryProperty.Satiety, 5.6f), new(CulinaryProperty.Warmth, 2.4f), new(CulinaryProperty.Heaviness, 1.2f)],
                SecondsRemaining = 33, OnsetSeconds = 12, DurationPercent = 135, History = ["Chop", "Marinate"], InputCount = 4,
            });
            window.Title = "Предпросмотр · " + window.Title; window.OpenCentered(); Export(args); return;
        }
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var apiary = new ApiaryWindow();
        var species = prototypes.EnumeratePrototypes<BeeSpeciesPrototype>().OrderBy(s => s.Tier).ThenBy(s => s.ID).Select(s =>
            new ApiarySpeciesState(s.ID, Loc.GetString($"bee-species-{s.ID.ToLowerInvariant()}"), s.Tier, s.CycleSeconds,
                s.Comb.Id, prototypes.Index(s.Comb).Name, s.RareProduct?.Id,
                s.RareProduct == null ? null : prototypes.Index(s.RareProduct.Value).Name,
                s.RareChance, s.RareGeneration, s.RareLineageSeconds / 60, s.NeedsMinerals)
            {
                Branch = s.Branch, MinTemperature = s.MinTemperature, MaxTemperature = s.MaxTemperature,
                Byproduct = s.Byproduct?.Id, ByproductName = s.Byproduct == null ? null : prototypes.Index(s.Byproduct.Value).Name,
                ByproductChance = s.ByproductChance,
            }).ToArray();
        apiary.Apply(new ApiaryState("apiary-status-working", "императорская", "", "", "", 0.6f, 130, 95, 12, 6, 4, true)
        {
            ActiveQueen = true, Generation = 5, LineageMinutes = 22, SecondsRemaining = 96, Temperature = 295.15f, Pressure = 101,
            FrameKind = "Selection", Species = species,
            Crosses = prototypes.EnumeratePrototypes<BeeMutationPrototype>().Select(p => new ApiaryMutationState(
                p.Result.Id, p.First.Id, p.Second.Id, p.Chance, p.Generation, p.LineageSeconds / 60, p.NeedsMinerals)).ToArray(),
            OutputSlots = Enumerable.Range(0, 20).Select(i => new WorkshopItemState(i, i % 3 == 0 ? "EclipseBeeDrone" : "EclipseFoodRoyalHoneycomb",
                i % 3 == 0 ? "Императорский трутень" : "Маточные соты")).ToArray(),
        });
        if (args.FirstOrDefault() == "selection") apiary.Tabs.CurrentTab = 1;
        apiary.Title = "Предпросмотр · " + apiary.Title; apiary.OpenCentered();
        Export(args);
    }
}
#endif
