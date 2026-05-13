using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BankingOffers.API.Entities;
    [Table("LoanProducts")]
    public class LoanProduct
    {
    [Key]
    public int Id { get; set; }
    public string BankName { get; set; }
    public string ProductName { get; set; }
    public decimal InterestRate { get; set; }
    public decimal MaxAmount { get; set; }
    public int MaxTermInMonths { get; set; }
    public string Url { get; set; }
    public DateTime LastUpdated { get; set; }
}

