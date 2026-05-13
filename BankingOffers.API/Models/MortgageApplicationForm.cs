using System.ComponentModel.DataAnnotations;

namespace BankingOffers.API.Models;

public class MortgageApplicationForm
{
    [Required(ErrorMessage = "Имя обязательно")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Фамилия обязательна")]
    public string LastName { get; set; } = "";

    [Required]
    [Range(10000, 100000000, ErrorMessage = "Укажите реальный доход")]
    public decimal IncomePerMonth { get; set; }

    [Required]
    [Range(100000, 1000000000, ErrorMessage = "Сумма кредита некорректна")]
    public decimal RequestedAmount { get; set; }

    [Required]
    [Range(100000, 1000000000, ErrorMessage = "Стоимость некорректна")]
    public decimal PropertyCost { get; set; }
}

// Модель для отображения заявки в профиле
public class MortgageApplicationView
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
    public decimal RequestedAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}