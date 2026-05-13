using System.ComponentModel.DataAnnotations;

namespace BankingOffers.API.DTOs;

public class MortgageRequestDto
{
    [Required] public string FirstName { get; set; } = "";
    [Required] public string LastName { get; set; } = "";
    [Required][Range(1, double.MaxValue)] public decimal IncomePerMonth { get; set; }
    [Required][Range(1, double.MaxValue)] public decimal RequestedAmount { get; set; }
    [Required][Range(1, double.MaxValue)] public decimal PropertyCost { get; set; }
}