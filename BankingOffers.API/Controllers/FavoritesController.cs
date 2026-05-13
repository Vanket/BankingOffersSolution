using BankingOffers.API.Data;
using BankingOffers.API.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BankingOffers.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public FavoritesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // Добавить/Удалить (лайк/дизлайк)
    [HttpPost("toggle/{productId}")]
    public async Task<IActionResult> Toggle(int productId)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var existing = await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.LoanProductId == productId);

        if (existing != null)
        {
            _context.Favorites.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { IsFavorite = false });
        }

        _context.Favorites.Add(new Favorite { UserId = userId, LoanProductId = productId });
        await _context.SaveChangesAsync();
        return Ok(new { IsFavorite = true });
    }

    // Получить список избранных предложений (полные данные)
    [HttpGet]
    public async Task<IActionResult> GetFavorites()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var products = await _context.Favorites
            .Where(f => f.UserId == userId)
            .Include(f => f.LoanProduct)
            .Select(f => f.LoanProduct)
            .ToListAsync();
        return Ok(products);
    }

    // Получить только ID (для закрашивания сердечек на главной)
    [HttpGet("ids")]
    public async Task<IActionResult> GetFavoriteIds()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var ids = await _context.Favorites
            .Where(f => f.UserId == userId)
            .Select(f => f.LoanProductId)
            .ToListAsync();
        return Ok(ids);
    }
}