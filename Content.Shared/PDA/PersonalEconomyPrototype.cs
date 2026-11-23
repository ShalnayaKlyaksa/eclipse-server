using Robust.Shared.Prototypes;

namespace Content.Shared.PDA;

[Prototype]
public sealed partial class PersonalEconomyPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = "";
    [DataField] public int InitialCredits = 1000;
    [DataField] public TimeSpan SalaryInterval = TimeSpan.FromMinutes(10);
    [DataField] public int DefaultSalary = 180;
    [DataField] public Dictionary<string, int> Salaries = new();
    [DataField] public int DocumentPrice = 5;
    [DataField] public Dictionary<string, int> VendorPrices = new();
}
