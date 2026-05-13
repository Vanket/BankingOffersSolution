using BankingOffers.API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BankingOffers.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ProductsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetLoanProducts()
    {
        var products = await _context.LoanProducts
            .ToListAsync(); 

        var sortedProducts = products.OrderBy(p => p.InterestRate).ToList();

        return Ok(sortedProducts);
    }
}