using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.CartridgeLoader.Cartridges;
using Content.Client.PDA;
using Content.IntegrationTests.Fixtures;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.PDA;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class PdaWorkspacesTest : GameTest
{
    private static T Field<T>(object target, string name) => (T) target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    [Test]
    public async Task TaskEditorPreservesCompletionAndSeparateDrafts()
    {
        await Client.WaitAssertion(() =>
        {
            using var fragment = new NanoTaskUiFragment();
            var completed = new NanoTaskItem("Completed task", "Cargo", true, NanoTaskPriority.High);
            var other = new NanoTaskItem("Other task", "Science", false, NanoTaskPriority.Low);
            fragment.UpdateState([new(1, completed), new(2, other)]);
            fragment.Editor.Select(1, completed);
            var description = Field<TextEdit>(fragment.Editor, "_description"); description.InsertAtCursor(" draft");
            var draft = fragment.Editor.MakeItem();
            Assert.That(draft.IsTaskDone, Is.True, "Editing a completed task must not silently reopen it.");
            fragment.Editor.Select(2, other); fragment.Editor.Select(1, completed);
            Assert.That(fragment.Editor.MakeItem().Description, Is.EqualTo(draft.Description));
            fragment.UpdateState([new(1, completed), new(2, other)]);
            Assert.That(fragment.Editor.MakeItem().Description, Is.EqualTo(draft.Description), "Server refreshes must not overwrite an unsaved edit.");
            Assert.That(new NanoTaskItem(new string('x', 256), "", false, NanoTaskPriority.Medium).Validate(), Is.True);
            Assert.That(new NanoTaskItem(new string('x', 257), "", false, NanoTaskPriority.Medium).Validate(), Is.False);
            Assert.That(new NanoTaskItem(" ", "", false, NanoTaskPriority.Medium).Validate(), Is.False);
            Assert.That(new NanoTaskItem("task", "", false, (NanoTaskPriority) 255).Validate(), Is.False);
            fragment.Editor.Select(null, null);
            description.InsertAtCursor("A new task that must survive until acknowledgment");
            var created = fragment.Editor.MakeItem();
            fragment.Editor.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fragment.Editor, []);
            Assert.That(fragment.Editor.MakeItem().Description, Is.EqualTo(created.Description), "Sending must not erase the draft before acknowledgment.");
            fragment.UpdateState([new(1, completed), new(2, other), new(3, created)]);
            Assert.That(fragment.Editor.EditingId, Is.EqualTo(3), "The new task becomes an edit after its server ID arrives.");
        });
    }

    [Test]
    public async Task AppearanceDraftSurvivesNavigationAndProfileRefresh()
    {
        await Client.WaitAssertion(() =>
        {
            using var menu = new PdaMenu();
            menu.ApplyDesktopProfile(new PdaDesktopProfileMessage(new PdaDesktopProfile { Owned = ["orbit", "studio"] }, 0, 0));
            var open = menu.GetType().GetMethod("OpenAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
            open.Invoke(menu, []);
            var page = menu.FindControl<BoxContainer>("AppearancePage");
            static IEnumerable<Control> Descendants(Control root) => root.Children.SelectMany(child => Descendants(child).Prepend(child));
            var text = Descendants(page).OfType<LineEdit>().Single();
            text.SetText("Unsaved desktop card", invokeEvent: true);
            menu.ToHomeScreen(); open.Invoke(menu, []);
            Assert.That(Descendants(page).OfType<LineEdit>().Single(), Is.SameAs(text));
            menu.ApplyDesktopProfile(new PdaDesktopProfileMessage(new PdaDesktopProfile { Owned = ["orbit", "studio"], CardText = "Server text" }, 0, 0));
            Assert.That(text.Text, Is.EqualTo("Unsaved desktop card"));
        });
    }

    [Test]
    public async Task ProbeRefreshDoesNotMutateServerOrderAndSearchRemainsApplied()
    {
        await Client.WaitAssertion(() =>
        {
            using var network = new NetProbeUiFragment();
            var devices = new List<ProbedNetworkDevice> { new("First", "ABC", "100", "Science"), new("Last", "DEF", "200", "Cargo") };
            network.UpdateState(devices); network.UpdateState(devices);
            Assert.That(devices.Select(device => device.Name), Is.EqualTo(new[] { "First", "Last" }));
            network.FindControl<LineEdit>("Search").SetText("cargo", invokeEvent: true);
            Assert.That(network.FindControl<Label>("Count").Text, Is.EqualTo("1/2"));
            network.UpdateState([..devices, new("Third", "GHI", "200", "Cargo")]);
            Assert.That(network.FindControl<Label>("Count").Text, Is.EqualTo("2/3"));
            using var logs = new LogProbeUiFragment();
            logs.UpdateState("Door", [new(System.TimeSpan.FromSeconds(30), "Anna"), new(System.TimeSpan.FromSeconds(45), "Boris")]);
            logs.FindControl<LineEdit>("Search").SetText("anna", invokeEvent: true);
            Assert.That(logs.FindControl<Label>("Count").Text, Is.EqualTo("1/2"));
        });
    }

    [Test]
    public async Task WantedRefreshKeepsSelectionAndCaseInsensitiveSearch()
    {
        await Client.WaitAssertion(() =>
        {
            using var wanted = new WantedListUiFragment();
            var anna = new WantedRecord(new GeneralStationRecord { Name = "Anna", JobTitle = "Scientist", Species = "Human" }, SecurityStatus.Wanted, "Questioning", "Security", []);
            var boris = new WantedRecord(new GeneralStationRecord { Name = "Boris", JobTitle = "Engineer", Species = "Human" }, SecurityStatus.Detained, "Fine", "Security", []);
            wanted.UpdateState([anna, boris]);
            wanted.GetType().GetMethod("ShowRecord", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wanted, [anna]);
            wanted.FindControl<LineEdit>("SearchBar").SetText("ANNA", invokeEvent: true);
            wanted.UpdateState([anna, boris]);
            Assert.That(wanted.FindControl<Label>("PersonName").Text, Is.EqualTo("Anna"));
            Assert.That(wanted.FindControl<BoxContainer>("PersonContainer").Visible, Is.True);
            wanted.FindControl<LineEdit>("SearchBar").SetText("nobody", invokeEvent: true);
            Assert.That(wanted.FindControl<BoxContainer>("PersonContainer").Visible, Is.False);
            wanted.FindControl<LineEdit>("SearchBar").SetText("", invokeEvent: true);
            Assert.That(wanted.FindControl<Label>("PersonName").Text, Is.EqualTo("Anna"));
        });
    }
}
