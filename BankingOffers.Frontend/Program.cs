using BankingOffers.Frontend;
using BankingOffers.Frontend.Providers; // <-- Добавить
using Blazored.LocalStorage; // <-- Добавить
using Microsoft.AspNetCore.Components.Authorization; // <-- Добавить
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
//Написал шнягу ниже что мы не менять адресацию каждый раз когда разворачиваю на сервере или дорабатываю на локалке
if (builder.HostEnvironment.IsDevelopment())
{
    // Локальная разработка: Жесткий адрес API
    builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("https://localhost:7004") });
}
else
{
    // Сервер (Release): Адрес сайта
    builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
}

builder.Services.AddMudServices();

// --- АВТОРИЗАЦИЯ ---
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
// -------------------

await builder.Build().RunAsync();