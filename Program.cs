using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

builder.Services
    .AddAuthentication(authenticationOptions => {
        authenticationOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        authenticationOptions.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("EntraId"));

static bool IsHtmx(HttpRequest httpRequest) =>
    string.Equals(httpRequest.Headers["Hx-Request"], "true", StringComparison.OrdinalIgnoreCase);

static string GetReturnUrl(HttpRequest httpRequest)
{
    // Prefer HX-Current-URL for HTMX requests
    var hxCurrentUrlValid = httpRequest.Headers.TryGetValue("Hx-Current-URL", out var currentUrl);

    if (hxCurrentUrlValid)
    {
        var currentUrlValid = Uri.TryCreate(currentUrl!, UriKind.Absolute, out var uri);

        if (currentUrlValid && uri is not null)
            return uri.PathAndQuery;
    }

    // Fallback for non-HTMX requests
    return httpRequest.Path + httpRequest.QueryString;
}

builder.Services.PostConfigure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme,
    cookieAuthenticationOptions =>{
        cookieAuthenticationOptions.LoginPath = "/login";
        cookieAuthenticationOptions.AccessDeniedPath = "/login";

        cookieAuthenticationOptions.Events ??= new CookieAuthenticationEvents();

        cookieAuthenticationOptions.Events.OnRedirectToLogin = redirectContext => {
            if (IsHtmx(redirectContext.Request))
            {
                redirectContext.Response.StatusCode = StatusCodes.Status401Unauthorized;

                var returnUrl = GetReturnUrl(redirectContext.Request);
                var hxRedirectUrl = $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
                redirectContext.Response.Headers["Hx-Redirect"] = hxRedirectUrl;

                return Task.CompletedTask;
            }

            redirectContext.Response.Redirect(redirectContext.RedirectUri);
            return Task.CompletedTask;
        };

        cookieAuthenticationOptions.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (string.Equals(ctx.Request.Headers["HX-Request"], "true", StringComparison.OrdinalIgnoreCase))
            {
                var returnUrl =
                    ctx.Request.Headers.TryGetValue("HX-Current-URL", out var currentUrl) &&
                    Uri.TryCreate(currentUrl!, UriKind.Absolute, out var uri) &&
                    uri is not null
                        ? uri.PathAndQuery
                        : ctx.Request.Path + ctx.Request.QueryString;

                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.Headers["HX-Redirect"] =
                    $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

                return Task.CompletedTask;
            }

            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.ConfigureApplicationCookie(cookieAuthenticationOptions => {
    cookieAuthenticationOptions.LoginPath = "/login";
    cookieAuthenticationOptions.AccessDeniedPath = "/login";

    cookieAuthenticationOptions.Events ??= new CookieAuthenticationEvents();

    cookieAuthenticationOptions.Events.OnRedirectToLogin = (redirectContext) => {
        if (IsHtmx(redirectContext.Request))
        {
            redirectContext.Response.StatusCode = StatusCodes.Status401Unauthorized;

            var returnUrl = GetReturnUrl(redirectContext.Request);
            var hxRedirectUrl = $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
            redirectContext.Response.Headers["HX-Redirect"] = hxRedirectUrl;

            return Task.CompletedTask;
        }

        redirectContext.Response.Redirect(redirectContext.RedirectUri);
        return Task.CompletedTask;
    };

    cookieAuthenticationOptions.Events.OnRedirectToAccessDenied = (redirectContext) => {
        if (IsHtmx(redirectContext.Request))
        {
            redirectContext.Response.StatusCode = StatusCodes.Status403Forbidden;

            var returnUrl = GetReturnUrl(redirectContext.Request);
            var hxRedirectUrl = $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
            redirectContext.Response.Headers["HX-Redirect"] = hxRedirectUrl;

            return Task.CompletedTask;
        }

        redirectContext.Response.Redirect(redirectContext.RedirectUri);
        return Task.CompletedTask;
    };
});

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

app.MapGet("/login", (HttpContext httpContext) => {
    var returnUrl = httpContext.Request.Query["returnUrl"].ToString();

    if (string.IsNullOrWhiteSpace(returnUrl))
        returnUrl = "/";

    return Results.Challenge(
        authenticationSchemes: [OpenIdConnectDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties { RedirectUri = returnUrl }
    );
});


app.MapGet("/logout", (HttpContext ctx) => {
    return Results.SignOut(
        new AuthenticationProperties {
            RedirectUri = "/"
        },
        authenticationSchemes: [
            OpenIdConnectDefaults.AuthenticationScheme,
            CookieAuthenticationDefaults.AuthenticationScheme
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
