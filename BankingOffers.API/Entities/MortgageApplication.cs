using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BankingOffers.API.Entities;

[Table("MortgageApplications")]
public class MortgageApplication
{
    [Key]
    public int Id { get; set; }
    public int UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public decimal IncomePerMonth { get; set; }
    public decimal RequestedAmount { get; set; }
    public decimal PropertyCost { get; set; }

    public string Status { get; set; } = "На рассмотрении";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
