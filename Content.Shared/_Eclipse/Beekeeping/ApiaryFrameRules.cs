namespace Content.Shared._Eclipse.Beekeeping;

public static class ApiaryFrameRules
{
    public static float MutationMultiplier(string kind, int tier) => tier >= 4 ? 1 : kind switch
    {
        "Selection" => 1.5f, "Production" => 1.2f, _ => 1,
    };
    public static int CombBonus(string kind) => kind switch { "Production" => 1, "Selection" => -1, _ => 0 };
    public static int DroneBonus(string kind) => kind == "Nursing" ? 2 : 0;
    public static float FoodMultiplier(string kind) => kind == "Nursing" ? 1.5f : 1;
    public static float WaterMultiplier(string kind) => kind == "Insulated" ? 0.75f : 1;
    public static float TemperatureTolerance(string kind) => kind == "Insulated" ? 8 : 0;
    public static int ForageMultiplier(string kind) => kind == "Forage" ? 2 : 1;
}
