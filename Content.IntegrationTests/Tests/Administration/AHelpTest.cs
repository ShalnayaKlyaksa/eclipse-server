using System.Linq;
using Content.Client.Administration.UI.Bwoink;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Follower.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests.Administration;

public sealed class AHelpTest : InteractionTest
{
    [Test]
    public async Task FollowPlayerFromEndOfListAfterEntityChanges()
    {
        EntityUid target = default;
        NetEntity targetNet = default;
        await Server.WaitPost(() =>
        {
            target = SEntMan.SpawnEntity(null, SEntMan.GetCoordinates(TargetCoords));
            targetNet = SEntMan.GetNetEntity(target);
        });

        Button follow = default!;
        await Client.WaitAssertion(() =>
        {
            var root = UiMan.WindowRoot;
            var originalBounds = root.SizeBox;
            var compactBounds = new UIBox2(0, 0, 640, 330);
            root.Arrange(compactBounds);
            try
            {
                UiMan.GetUIController<AHelpUIController>().Open();
                var window = GetWindow<BwoinkWindow>();
                var selector = window.Bwoink.ChannelSelector;
                var localPlayer = selector.PlayerInfo.Single(info => info.SessionId == ClientSession.UserId);
                var players = Enumerable.Range(1, 20).Select(number => localPlayer with
                {
                    Username = $"Player{number}",
                    SessionId = new NetUserId(Guid.NewGuid()),
                    NetEntity = null,
                }).ToArray();
                selector.PopulateList(players);
                root.Arrange(compactBounds);

                var list = selector.PlayerListContainer;
                var scrollbar = list.Children.OfType<VScrollBar>().Single();
                Assert.That(scrollbar.Visible, Is.True);
                scrollbar.Value = scrollbar.MaxValue;
                list.Arrange(UIBox2.FromDimensions(list.Position, list.Size));

                var last = list.Children.OfType<ListContainerButton>().Last();
                Assert.That(last.Data, Is.EqualTo(list.Data.Last()));
                list.Select(last.Data);

                var selected = ((PlayerListData) last.Data).Info;
                var index = Array.IndexOf(players, selected);
                players[index] = selected with { NetEntity = targetNet };
                selector.PopulateList(players);

                follow = window.Bwoink.FindControl<Button>("Follow");
                Assert.That(follow.Disabled, Is.False);
                Assert.That(follow.GlobalRect.Bottom, Is.LessThanOrEqualTo(root.GlobalRect.Bottom));
            }
            finally
            {
                root.Arrange(originalBounds);
            }
        });

        await ClickControl(follow);
        await RunTicks(5);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<FollowerComponent>(SPlayer).Following, Is.EqualTo(target)));
    }
}
