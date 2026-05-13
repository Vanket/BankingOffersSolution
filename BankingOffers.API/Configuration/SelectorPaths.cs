namespace BankingOffers.API.Configuration;
using System.Collections.Generic;
public class SelectorPaths
{
    public string ProductContainer { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductUrl { get; set; } = "";
    public string AdditionalInfo { get; set; } = "";


    public string InterestRate { get; set; } = "";
    public string MaxAmount { get; set; } = "";
    public string MaxTerm { get; set; } = "";

    public string DetailInterestRate { get; set; } = "";
    public string DetailMaxAmount { get; set; } = "";
    public string DetailMaxTerm { get; set; } = "";

    public Dictionary<string, string> DetailPageSelectors { get; set; } = new();
}