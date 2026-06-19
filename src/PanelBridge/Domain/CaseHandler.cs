namespace PanelBridge.Domain;

public class CaseHandler
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string Telephone { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}
