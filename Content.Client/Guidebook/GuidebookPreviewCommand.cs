#if DEBUG
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Guidebook.Controls;
using Content.Client.UserInterface.ControlExtensions;
using Content.Shared.Guidebook;
using Content.Shared.Administration;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Asynchronous;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;

namespace Content.Client.Guidebook;

/// <summary>Reproducible offline visual review of the actual guide UI, including markup and embedded examples.</summary>
[AnyCommand]
public sealed class GuidebookPreviewCommand : IConsoleCommand
{
    public string Command => "guidebook-preview";
    public string Description => "Preview the guidebook and optionally capture a screenshot (DEBUG only).";
    public string Help => "guidebook-preview article|example|catalog|search <entry-id-or-query> [screenshot.png] [quit]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || args[0] is not ("article" or "example" or "catalog" or "search")) { shell.WriteLine(Help); return; }
        var tasks = IoCManager.Resolve<ITaskManager>();
        try
        {
            await Task.Delay(1500);
            var ready = new TaskCompletionSource();
            tasks.RunOnMainThread(() =>
            {
                try
                {
                    var client = IoCManager.Resolve<IBaseClient>();
                    if (client.RunLevel == ClientRunLevel.Initialize) client.StartSinglePlayer();
                    ready.SetResult();
                }
                catch (Exception e) { ready.SetException(e); }
            });
            await ready.Task;
            await Task.Delay(500);
            var opened = new TaskCompletionSource();
            tasks.RunOnMainThread(() =>
            {
                try
                {
                    var entries = IoCManager.Resolve<IPrototypeManager>().EnumeratePrototypes<GuideEntryPrototype>()
                        .ToDictionary(e => new ProtoId<GuideEntryPrototype>(e.Id), e => (GuideEntry) e);
                    var window = new GuidebookWindow();
                    ProtoId<GuideEntryPrototype>? selected = null;
                    if (args[0] is "article" or "example") selected = new ProtoId<GuideEntryPrototype>(args[1]);
                    window.UpdateGuides(entries, selected: selected);
                    window.OpenGuide();
                    if (args[0] == "example")
                    {
                        foreach (var example in window.GetControlOfType<GuideMechanismExample>(true))
                            foreach (var foldout in example.GetControlOfType<Collapsible>(true)) foldout.BodyVisible = true;
                        ScrollToExample(window);
                    }
                    if (args[0] == "search") window.SearchFor(args[1]);
                    opened.SetResult();
                }
                catch (Exception e) { opened.SetException(e); }
            });
            await opened.Task;
            if (args.Length < 3) return;
            await Task.Delay(2000);
            tasks.RunOnMainThread(() => IoCManager.Resolve<IClyde>().Screenshot(ScreenshotType.Final, image =>
            {
                try
                {
                    var resources = IoCManager.Resolve<IResourceManager>();
                    var folder = new ResPath("/Screenshots/Guidebook");
                    resources.UserData.CreateDir(folder);
                    using var file = resources.UserData.Open(folder / new ResPath(args[2]).Filename, FileMode.Create, FileAccess.Write, FileShare.None);
                    image.SaveAsPng(file);
                    image.Dispose();
                    shell.WriteLine("Guidebook screenshot saved.");
                }
                catch (Exception e) { shell.WriteError(e.ToString()); }
                finally
                {
                    if (args.Length > 3 && args[3] == "quit") IoCManager.Resolve<IClientConsoleHost>().ExecuteCommand("quit");
                }
            }));
        }
        catch (Exception e) { shell.WriteError(e.ToString()); }
    }

    private static void ScrollToExample(GuidebookWindow window)
    {
        IoCManager.Resolve<Robust.Client.UserInterface.IUserInterfaceManager>().DeferAction(() =>
            window.FindControl<ScrollContainer>("Scroll").VScrollTarget = float.MaxValue);
    }
}
#endif
