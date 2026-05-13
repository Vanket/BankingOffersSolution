using BankingOffers.API.Data;
using BankingOffers.API.DTOs;
using BankingOffers.API.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BankingOffers.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize] // Доступ только авторизованным
public class MortgagesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public MortgagesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // Отправить заявку
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(MortgageRequestDto dto)
    {
        // Узнаем ID текущего пользователя из токена
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var app = new MortgageApplication
        {
            UserId = userId,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            IncomePerMonth = dto.IncomePerMonth,
            RequestedAmount = dto.RequestedAmount,
            PropertyCost = dto.PropertyCost,
            CreatedAt = DateTime.UtcNow
        };

        _context.MortgageApplications.Add(app);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // Получить мои заявки
    [HttpGet("my")]
    public async Task<IActionResult> GetMyApplications()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var apps = await _context.MortgageApplications
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        return Ok(apps);
    }
}