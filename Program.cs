using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using MinimalHtmxRazor.Rendering;
using MinimalHtmxRazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor support (we reuse the view engine)
builder.Services.AddRazorPages();
builder.Services.AddControllersWithViews();

// Render Razor partials to string
builder.Services.AddScoped<RazorViewStringRenderer>();

// HttpClient for JSONPlaceholder
builder.Services.AddHttpClient<JsonPlaceholderClient>(httpClient => {
    var uriString = builder.Configuration["JsonPlaceholder:BaseUrl"] 
        ?? "https://jsonplaceholder.typicode.com/"; 

    httpClient.BaseAddress = new Uri(uriString);
});

// Entra ID web app sign-in (cookie + OpenID Connect)
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("EntraId"));

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();



// =============================================================================
// Endpoints
// =============================================================================


// Authentication

app.MapGet("/login", (HttpContext ctx) => {
    return Results.Challenge(
        authenticationSchemes: [OpenIdConnectDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties {
            RedirectUri = "/"
        }
    );
});

app.MapGet("/logout", (HttpContext ctx) => {
    return Results.SignOut(
        new AuthenticationProperties {
            RedirectUri = "/"
        },
        authenticationSchemes: [
            OpenIdConnectDefaults.AuthenticationScheme,
            "Cookies"
        ]
    );
});


// JSON API

app.MapGet("/api/posts", async (
    JsonPlaceholderClient jsonPlaceholderClient, 
    CancellationToken cancellationToken
) => {
    var posts = await jsonPlaceholderClient.GetPostsAsync(cancellationToken);
    return Results.Ok(posts);
});

app.MapGet("/api/posts/{id:int}", async (
    int id, 
    JsonPlaceholderClient jsonPlaceholderClient, 
    CancellationToken cancellationToken
) => {
    if (id <= 0) 
        return Results.BadRequest(new { error = "id must be > 0" });

    var model = await jsonPlaceholderClient.GetPostWithCommentsAsync(id, cancellationToken);
    return model is null ? Results.NotFound() : Results.Ok(model);
});


// HTMX API

app.MapGet("/htmx/posts", async (
    JsonPlaceholderClient jsonPlaceholderClient,
    RazorViewStringRenderer razorViewStringRenderer,
    CancellationToken cancellationToken
) => {
    var posts = await jsonPlaceholderClient.GetPostsAsync(cancellationToken);
    var html = await razorViewStringRenderer.RenderPartialAsync("_PostsList", posts);
    return Results.Content(html, "text/html");
});

app.MapGet("/htmx/posts/{id:int}", async (
    int id,
    JsonPlaceholderClient jsonPlaceholderClient,
    RazorViewStringRenderer razorViewStringRenderer,
    CancellationToken cancellationToken
) => {
    if (id <= 0) 
        return Results.BadRequest("id must be > 0");

    var model = await jsonPlaceholderClient.GetPostWithCommentsAsync(id, cancellationToken);

    if (model is null) 
        return Results.NotFound();

    var html = await razorViewStringRenderer.RenderPartialAsync("_PostDetail", model);
    return Results.Content(html, "text/html");
}).RequireAuthorization(); // Example: protect details behind login


app.Run();
