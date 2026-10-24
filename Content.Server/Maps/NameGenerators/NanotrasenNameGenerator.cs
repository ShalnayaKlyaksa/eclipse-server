using JetBrains.Annotations;
using Robust.Shared.Random;

namespace Content.Server.Maps.NameGenerators;

[UsedImplicitly]
public sealed partial class NanotrasenNameGenerator : StationNameGenerator
{
    /// <summary>
    ///     Where the map comes from. Should be a two or three letter code, for example "VG" for Packedstation.
    /// </summary>
    [DataField("prefixCreator")] public string PrefixCreator = default!;

    public override string FormatName(string input)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        var suffixCodes = new[] { "LV", "NX", "EV", "QT", "PR" };

        return string.Format(input, $"NT{PrefixCreator}", $"{random.Pick(suffixCodes)}-{random.Next(0, 999):D3}");
    }
}
