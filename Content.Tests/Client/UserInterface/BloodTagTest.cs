using Content.Client.RichText;
using Content.Client.UserInterface.RichText;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.Tests.Client.UserInterface;

[TestFixture]
public sealed class BloodTagTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    [OneTimeSetUp]
    public void Setup() => IoCManager.Resolve<IUserInterfaceManager>().InitializeTesting();

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("death\nnext line")]
    [TestCase("death\rnext line")]
    [TestCase("123456789012345678901234567890123")]
    public void RejectsInvalidCaptions(string text)
    {
        var tag = new BloodTag();
        Assert.That(tag.TryCreateControl(new MarkupNode("blood", new MarkupParameter(text), null), out var control), Is.False);
        Assert.That(control, Is.Null);
    }

    [Test]
    public void EachOccurrenceOwnsItsAnimationAndKeepsTheCaption()
    {
        var tag = new BloodTag();
        var node = new MarkupNode("blood", new MarkupParameter("смерть"), null);
        Assert.That(tag.TryCreateControl(node, out var first), Is.True);
        Assert.That(tag.TryCreateControl(node, out var second), Is.True);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(((Label) first).Text, Is.EqualTo("смерть"));
        Assert.That(first.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
        Assert.That(UserFormattableTags.BaseAllowedTags, Does.Not.Contain(typeof(BloodTag)));
        first.Dispose();
        second.Dispose();
    }
}
