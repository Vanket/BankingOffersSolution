namespace BankingOffers.API.Configuration;

public class ScrapingTarget
{
    public string BankName { get; set; }
    public string Url { get; set; }
    public SelectorPaths Selectors { get; set; }

}