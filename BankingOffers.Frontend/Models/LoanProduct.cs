namespace BankingOffers.Frontend.Models;

public class LoanProduct
{
    public int Id { get; set; }
    public string BankName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal InterestRate { get; set; }
    public decimal MaxAmount { get; set; }
    public int MaxTermInMonths { get; set; }

    // ВОТ ЭТА СТРОКА, КОТОРОЙ НЕ ХВАТАЛО
    public string? Url { get; set; }

    public DateTime LastUpdated { get; set; }
}