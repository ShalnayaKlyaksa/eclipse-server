using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared._Eclipse.Cooking;

[RegisterComponent]
public sealed partial class CulinaryStationComponent : Component
{
    [DataField] public int MaxIngredients = 12;
    [DataField] public float Capacity = 300;
    [DataField] public CulinaryMethod[] AllowedMethods = [CulinaryMethod.Boil, CulinaryMethod.Simmer, CulinaryMethod.Reduce];
    public Container Ingredients = default!;
    public CulinaryMethod Method = CulinaryMethod.Boil;
    public float Progress;
    public bool Working;
    public bool Ready;
    public string Status = "culinary-status-idle";
    [DataField] public List<string> Composition = new();
    [DataField] public HashSet<string> Flavors = new();
}

/// <summary>Only actors currently digesting culinary properties enter the nutrition update query.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CulinaryNutritionComponent : Component
{
    [DataField, AutoNetworkedField] public float Recovery;
    [DataField, AutoNetworkedField] public float Efficiency;
    [DataField, AutoNetworkedField] public float Heaviness;
    public float[] Doses = new float[13];
    public float[] Pending = new float[13];
    public float[] Delays = new float[13];
    public float[] Strengths = new float[13];
}

[Serializable, NetSerializable]
public enum CulinaryUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class CulinaryState(string[] ingredients, string properties, float volume,
    CulinaryMethod method, float progress, bool working, bool ready, bool powered, string status) : BoundUserInterfaceState
{
    public string[] Ingredients = ingredients;
    public string Properties = properties;
    public float Volume = volume;
    public CulinaryMethod Method = method;
    public float Progress = progress;
    public bool Working = working;
    public bool Ready = ready;
    public bool Powered = powered;
    public string Status = status;
    public WorkshopItemState[] InputSlots = [];
    public CulinaryPropertyState[] PropertyValues = [];
    public string[] History = [];
    public float SecondsRemaining;
    public int InputCount;
    public float Capacity = 300;
    public float OnsetSeconds;
    public float DurationPercent;
    public string StationName = "";
    public CulinaryMethod[] AllowedMethods = [CulinaryMethod.Boil, CulinaryMethod.Simmer, CulinaryMethod.Reduce];
    public bool MethodDone;
}

[Serializable, NetSerializable]
public sealed class CulinaryPropertyState(CulinaryProperty property, float value)
{
    public CulinaryProperty Property = property;
    public float Value = value;
}

[Serializable, NetSerializable]
public sealed class CulinarySlotMessage(int index) : BoundUserInterfaceMessage
{
    public int Index = index;
}

[Serializable, NetSerializable]
public sealed class CulinaryMethodMessage(CulinaryMethod method) : BoundUserInterfaceMessage
{
    public CulinaryMethod Method = method;
}

[Serializable, NetSerializable]
public sealed class CulinaryActionMessage(CulinaryAction action) : BoundUserInterfaceMessage
{
    public CulinaryAction Action = action;
}

[Serializable, NetSerializable]
public enum CulinaryAction : byte { Start, Stop, Serve, Eject, Taste, InsertHeld }

[RegisterComponent]
public sealed partial class CulinaryPreparationToolComponent : Component;

[RegisterComponent]
public sealed partial class CulinaryVesselComponent : Component
{
    [DataField] public List<string> Composition = new();
    [DataField] public HashSet<string> Flavors = new();
}

[Serializable, NetSerializable]
public sealed partial class CulinaryPrepareDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CulinaryStirDoAfterEvent : SimpleDoAfterEvent;
