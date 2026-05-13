namespace BankingOffers.API.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty; // Храним хеш, а не пароль!
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}