using System.Collections.Generic;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Chat;

namespace Content.IntegrationTests.Tests._Eclipse;

[TestFixture]
public sealed class ForgeChatFilterTest : GameTest
{
    [Test]
    public async Task PresetsPreserveUnavailableChannelsAndNotifyOnlyChanges()
    {
        await Client.WaitAssertion(() =>
        {
            using var popup = new ChannelFilterPopup();
            var available = ChatChannel.Local | ChatChannel.Radio | ChatChannel.Server;
            popup.SetChannels(available);
            popup.SetActive(ChatChannel.Admin, false);
            var changes = new List<(ChatChannel, bool)>();
            popup.OnChannelFilter += (channel, enabled) => changes.Add((channel, enabled));

            popup.ApplyPreset(ChatChannel.Local | ChatChannel.Radio);
            Assert.That(popup.GetActive(), Is.EqualTo(ChatChannel.Local | ChatChannel.Radio));
            Assert.That(popup.HasDisabledChannels, Is.True);
            Assert.That(changes, Is.EqualTo(new[] { (ChatChannel.Server, false) }));
            Assert.That(popup.IsActive(ChatChannel.Admin), Is.False);

            changes.Clear();
            popup.ApplyPreset(ChatChannel.Local | ChatChannel.Radio);
            Assert.That(changes, Is.Empty);

            popup.ApplyPreset(null);
            Assert.That(popup.GetActive(), Is.EqualTo(available));
            Assert.That(popup.HasDisabledChannels, Is.False);
            Assert.That(popup.IsActive(ChatChannel.Admin), Is.False);
            Assert.That(changes, Is.EqualTo(new[] { (ChatChannel.Server, true) }));
        });
    }
}
