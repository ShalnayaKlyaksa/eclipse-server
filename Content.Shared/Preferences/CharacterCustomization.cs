using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Preferences;

[DataDefinition, Serializable, NetSerializable]
public partial record struct CharacterCustomization
{
    [DataField] public VulpkaninBodyType BodyType { get; set; }
    [DataField] public int Height { get; set; }
    [DataField] public int Weight { get; set; }
    [DataField] public VocalStyle VocalStyle { get; set; }
    [DataField] public ErpPreference Erp { get; set; }
    [DataField] public CharacterHistory Virginity { get; set; }
    [DataField] public CharacterHistory AnalVirginity { get; set; }

    public static bool HasBodyTypes(ProtoId<SpeciesPrototype> species) => species == "Vulpkanin";

    public CharacterCustomization Validated(SpeciesPrototype species) => this with
    {
        BodyType = HasBodyTypes(species.ID) && Enum.IsDefined(BodyType) ? BodyType : default,
        Height = Height == 0 ? 0 : Math.Clamp(Height, species.MinHeight, species.MaxHeight),
        Weight = Weight == 0 ? 0 : Math.Clamp(Weight, species.MinWeight, species.MaxWeight),
        VocalStyle = Enum.IsDefined(VocalStyle) ? VocalStyle : default,
        Erp = Enum.IsDefined(Erp) ? Erp : default,
        Virginity = Enum.IsDefined(Virginity) ? Virginity : default,
        AnalVirginity = Enum.IsDefined(AnalVirginity) ? AnalVirginity : default,
    };

    public Sex GetVocalSex(Sex sex) => VocalStyle switch
    {
        VocalStyle.Masculine => Sex.Male,
        VocalStyle.Feminine => Sex.Female,
        _ => sex.PresentationSex(),
    };
}

[Serializable, NetSerializable]
public enum VulpkaninBodyType : byte
{
    Default,
    CurvedShortMuzzle,
    CurvedLongMuzzle,
    StraightShortMuzzle,
    StraightLongMuzzle,
}

[Serializable, NetSerializable]
public enum VocalStyle : byte
{
    Default,
    Masculine,
    Feminine,
}

[Serializable, NetSerializable]
public enum ErpPreference : byte
{
    Ask,
    No,
    Yes,
}

[Serializable, NetSerializable]
public enum CharacterHistory : byte
{
    Unspecified,
    No,
    Yes,
}
