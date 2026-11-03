namespace FrontierAirportBuilder.Workflow;

/// <summary>Small career policy adapter. Future commercial sources can use PassengerTransport without this class or wages.</summary>
public static class EmployedPassengerMissions
{
    public static bool SupportsType(string category, string type) => category switch
    {
        "Offshore" => type == "vision-jet",
        "Missionary" => type == "c208",
        _ => false
    };
    public static int Count(string category) => category == "Missionary" ? 2 : 1;
    public static int Count(string category, string airport) => category == "Offshore" && airport is "FYWE" or "FYWH" ? 2 : Count(category);
    public static string Purpose(string category) => category == "Missionary" ? "Doctor and community liaison visiting a community" : "Specialist / executive transfer";
    public static string Purpose(string category, string airport) => category == "Offshore" && airport is "FYWE" or "FYWH"
        ? "Engineering team — two technical specialists travelling to Windhoek" : Purpose(category);
}
