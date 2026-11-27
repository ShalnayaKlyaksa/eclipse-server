using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Humanoid.Markings;

/// <summary>
///     Represents a marking ID and its colors
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public partial record struct Marking
{
    /// <summary>
    /// The <see cref="MarkingPrototype"/> referred to by this marking
    /// </summary>
    [DataField(required: true)]
    public ProtoId<MarkingPrototype> MarkingId;

    [DataField("markingColor")]
    private List<Color> _markingColors;

    /// <summary>
    /// The colors taken on by the marking
    /// </summary>
    public IReadOnlyList<Color> MarkingColors => _markingColors;

    [DataField]
    public MarkingGradient? Gradient;

    /// <summary>
    /// Whether the marking is forced regardless of points
    /// </summary>
    public bool Forced;

    public Marking()
    {
        _markingColors = new();
    }

    public Marking(ProtoId<MarkingPrototype> markingId, IEnumerable<Color> colors)
    {
        MarkingId = markingId;
        _markingColors = colors.ToList();
    }

    public Marking(ProtoId<MarkingPrototype> markingId, int colorsCount) : this(markingId,
        Enumerable.Repeat(Color.White, colorsCount).ToList())
    {
    }

    public bool Equals(Marking other)
    {
        return MarkingId.Equals(other.MarkingId)
            && MarkingColors.SequenceEqual(other.MarkingColors)
            && Gradient == other.Gradient
            && Forced.Equals(other.Forced);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MarkingId);
        foreach (var color in MarkingColors)
            hash.Add(color);
        hash.Add(Gradient);
        hash.Add(Forced);
        return hash.ToHashCode();
    }

    public Marking WithColor(Color color) =>
        this with { _markingColors = Enumerable.Repeat(color, MarkingColors.Count).ToList() };

    public Marking WithColorAt(int index, Color color)
    {
        var newColors = _markingColors.ShallowClone();
        newColors[index] = color;
        return this with { _markingColors = newColors };
    }

    // look this could be better but I don't think serializing
    // colors is the correct thing to do
    //
    // this is still janky imo but serializing a color and feeding
    // it into the default JSON serializer (which is just *fine*)
    // doesn't seem to have compatible interfaces? this 'works'
    // for now but should eventually be improved so that this can,
    // in fact just be serialized through a convenient interface
    public string ToLegacyDbString()
    {
        // reserved character
        string sanitizedName = MarkingId.Id.Replace('@', '_');
        List<string> colorStringList = new();
        foreach (var color in MarkingColors)
            colorStringList.Add(color.ToHex());

        return $"{sanitizedName}@{String.Join(',', colorStringList)}";
    }

    public static Marking? ParseFromDbString(string input)
    {
        if (input.Length == 0) return null;
        var split = input.Split('@');
        if (split.Length != 2) return null;
        List<Color> colorList = new();
        foreach (string color in split[1].Split(','))
        {
            colorList.Add(Color.FromHex(color));
        }

        return new Marking(split[0], colorList);
    }
}

[DataDefinition, Serializable, NetSerializable]
public partial record struct MarkingGradient()
{
    public const int MinSteps = 2;
    public const int MaxSteps = 6;
    public const int DefaultSteps = 3;
    public const int MaxExtraColors = 6;

    [DataField]
    public Color TipColor = Color.White;

    [DataField]
    public int Steps = DefaultSteps;

    [DataField]
    public MarkingGradientShape Shape = MarkingGradientShape.Contour;

    [DataField]
    public MarkingGradientDirection Direction = MarkingGradientDirection.Down;

    [DataField] public int? AngleDegrees;
    [DataField] public int? Start;
    [DataField] public int? Length;
    [DataField] public int CenterX = 50;
    [DataField] public int CenterY = 50;
    [DataField] public MarkingGradientStop[]? Stops;

    public int EffectiveAngle => AngleDegrees ?? (Direction switch
    {
        MarkingGradientDirection.Right => 0,
        MarkingGradientDirection.Left => 180,
        MarkingGradientDirection.Up => 270,
        _ => 90,
    });

    public int EffectiveStart => Start ?? (Shape == MarkingGradientShape.Contour ? 22 : 0);
    public int EffectiveLength => Length ?? (100 - (Shape == MarkingGradientShape.Contour ? 22 : 0));

    public MarkingGradient Normalized() => this with
    {
        TipColor = HumanoidCharacterAppearance.ClampColor(TipColor),
        Steps = Math.Clamp(Steps, MinSteps, MaxSteps),
        Shape = Enum.IsDefined(Shape) ? Shape : MarkingGradientShape.Contour,
        Direction = Enum.IsDefined(Direction) ? Direction : MarkingGradientDirection.Down,
        AngleDegrees = AngleDegrees is { } angle ? ((angle % 360) + 360) % 360 : null,
        Start = Start is { } start ? Math.Clamp(start, 0, 100) : null,
        Length = Length is { } length ? Math.Clamp(length, 1, 100) : null,
        CenterX = Math.Clamp(CenterX, 0, 100),
        CenterY = Math.Clamp(CenterY, 0, 100),
        Stops = Stops is { Length: > 0 } ? Stops.Take(MaxExtraColors)
            .Where(stop => stop.Position is > 0 and < 100)
            .DistinctBy(stop => stop.Position)
            .OrderBy(stop => stop.Position)
            .Select(stop => stop with { Color = HumanoidCharacterAppearance.ClampColor(stop.Color) })
            .ToArray() : null,
    };

    public bool Equals(MarkingGradient other) =>
        TipColor == other.TipColor && Steps == other.Steps && Shape == other.Shape &&
        Direction == other.Direction && AngleDegrees == other.AngleDegrees && Start == other.Start && Length == other.Length &&
        CenterX == other.CenterX && CenterY == other.CenterY && Stops.AsSpan().SequenceEqual(other.Stops);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(HashCode.Combine(TipColor, Steps, Shape, Direction, AngleDegrees, Start, Length));
        hash.Add(CenterX);
        hash.Add(CenterY);
        foreach (var stop in Stops.AsSpan())
            hash.Add(stop);
        return hash.ToHashCode();
    }
}

[DataDefinition, Serializable, NetSerializable]
public partial record struct MarkingGradientStop()
{
    [DataField] public int Position;
    [DataField] public Color Color;
}

[Serializable, NetSerializable]
public enum MarkingGradientShape : byte
{
    Contour,
    Linear,
    Radial,
}

[Serializable, NetSerializable]
public enum MarkingGradientDirection : byte
{
    Down,
    Up,
    Right,
    Left,
}
