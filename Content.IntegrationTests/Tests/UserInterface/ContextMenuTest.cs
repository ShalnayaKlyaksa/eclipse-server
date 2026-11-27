using Content.Client.ContextMenu.UI;
using Content.IntegrationTests.Fixtures;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests.UserInterface;

public sealed class ContextMenuTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task DisposingEntityEntryClosesItsOpenSubMenus()
    {
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var context = ui.GetUIController<ContextMenuUIController>();
            var entityMenu = ui.GetUIController<EntityMenuUIController>();
            context.Setup();
            try
            {
                var entity = CSpawn("DrinkGlass");
                entityMenu.OpenRootMenu([entity]);
                var entry = entityMenu.Elements[entity];
                var popup = entry.SubMenu!;
                context.OpenSubMenu(entry);

                var category = new ContextMenuElement();
                context.AddElement(popup, category);
                var categoryPopup = new ContextMenuPopup(context, category);
                context.OpenSubMenu(category);
                Assert.That(context.Menus.Peek(), Is.SameAs(categoryPopup));

                entry.Dispose();

                Assert.That(popup.Disposed, Is.True);
                Assert.That(categoryPopup.Disposed, Is.True);
                Assert.That(context.Menus, Is.EqualTo(new[] { context.RootMenu }));
            }
            finally
            {
                entityMenu.Elements.Clear();
                context.Shutdown();
            }
        });
    }
}
