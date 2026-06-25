namespace Api.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;      // unique, lower-cased
    public string PasswordHash { get; set; } = string.Empty; // "salt:hash" (PBKDF2)
    public string Role { get; set; } = "Tech";              // Admin | Tech
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
