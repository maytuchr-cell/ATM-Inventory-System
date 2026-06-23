namespace Api.Models;

public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    // DHL_CENTER | RATCHABURANA | GRG | OL_TECHNICIAN | IN_TRANSIT | TRANSPORT_HUB | AIRPORT | SCRAP | LOCAL_VENDOR
    public string LocationType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
