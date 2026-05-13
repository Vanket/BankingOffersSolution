namespace BankingOffers.API.Entities;

public class Favorite
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public int LoanProductId { get; set; }
    public LoanProduct? LoanProduct { get; set; } // Связь с продуктом
}