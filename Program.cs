using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using KabuMemo;
using KabuMemo.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<GitHubService>();
builder.Services.AddScoped(_ => new StockApiService(
    new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
));
builder.Services.AddScoped<DisclosureService>(sp => new DisclosureService(
    new HttpClient { Timeout = TimeSpan.FromSeconds(15) },
    sp.GetRequiredService<IJSRuntime>()
));
builder.Services.AddScoped<WatchListService>();
builder.Services.AddScoped<WatchListStateService>();

await builder.Build().RunAsync();
