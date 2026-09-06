namespace HabloTruckPlatform.Domain.Access;

[Flags]
public enum AccessSource
{
    None = 0,
    Individual = 1,
    Company = 2,
    Both = Individual | Company
}