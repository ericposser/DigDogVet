using DigDog.Data;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

namespace DigDog;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ── Banco de Dados ─────────────────────────────────────────────────
        var connectionString = (builder.Configuration.GetConnectionString("DefaultConnection") ?? "")
                               + ";MaximumPoolSize=50;MinimumPoolSize=20;ConnectionTimeout=30;ConnectionLifeTime=300;";

        builder.Services.AddDbContext<Contexto>(opcoes =>
            opcoes.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        builder.Services.AddDbContextFactory<Contexto>(opcoes =>
            opcoes.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)),
            ServiceLifetime.Scoped);

        builder.Services.AddDataProtection();
        builder.Services.AddSignalR();
        builder.Services.AddMemoryCache();

        builder.Services.AddScoped<PermissaoService>();
        builder.Services.AddScoped<LogService>();
        builder.Services.AddHostedService<LimpezaLogService>();
        builder.Services.AddScoped<CaktoWebhookService>();

        // ── Proxy reverso (Square Cloud termina HTTPS na borda) ───────────
        builder.Services.Configure<ForwardedHeadersOptions>(opcoes =>
        {
            opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            opcoes.KnownNetworks.Clear();
            opcoes.KnownProxies.Clear();
        });

        // ── Identity ───────────────────────────────────────────────────────
        builder.Services.AddDefaultIdentity<IdentityUser>(opcoes =>
        {
            opcoes.Password.RequireDigit           = false;
            opcoes.Password.RequiredLength         = 3;
            opcoes.Password.RequireUppercase       = false;
            opcoes.Password.RequireNonAlphanumeric = false;
            opcoes.Password.RequireLowercase       = false;
            opcoes.SignIn.RequireConfirmedAccount  = false;

            opcoes.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(5);
            opcoes.Lockout.MaxFailedAccessAttempts = 5;
            opcoes.Lockout.AllowedForNewUsers      = true;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<Contexto>();

        // ── Cookie de Autenticação ─────────────────────────────────────────
        builder.Services.ConfigureApplicationCookie(opcoes =>
        {
            opcoes.LoginPath        = "/Identity/Account/Login";
            opcoes.LogoutPath       = "/Identity/Account/Logout";
            opcoes.AccessDeniedPath = "/Erro/AcessoNegado";

            opcoes.Cookie.HttpOnly     = true;
            opcoes.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            opcoes.Cookie.SameSite     = SameSiteMode.Lax;          // ← Strict quebrava redirects após login
            opcoes.Cookie.Name         = "DigDog.Auth";              // ← __Host- exige HTTPS direto no app
            opcoes.ExpireTimeSpan      = TimeSpan.FromHours(8);
            opcoes.SlidingExpiration   = true;

            opcoes.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
        });

        builder.Services.Configure<SecurityStampValidatorOptions>(opcoes =>
        {
            opcoes.ValidationInterval = TimeSpan.FromMinutes(30);
        });

        // ── Rate Limiting (TODOS particionados por IP) ─────────────────────
        builder.Services.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Tentativas de login por IP — apenas POST do Login deveria usar isto
            opcoes.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window      = TimeSpan.FromMinutes(5),
                        PermitLimit = 10,
                        QueueLimit  = 0
                    }));

            opcoes.AddPolicy("publica", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        Window            = TimeSpan.FromMinutes(1),
                        PermitLimit       = 30,
                        SegmentsPerWindow = 6,
                        QueueLimit        = 0
                    }));

            opcoes.AddPolicy("webhook", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window      = TimeSpan.FromMinutes(1),
                        PermitLimit = 60,
                        QueueLimit  = 5
                    }));

            opcoes.AddPolicy("geral", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        Window            = TimeSpan.FromMinutes(1),
                        PermitLimit       = 120,
                        SegmentsPerWindow = 6,
                        QueueLimit        = 10
                    }));

            opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        Window            = TimeSpan.FromMinutes(1),
                        PermitLimit       = 200,
                        SegmentsPerWindow = 6,
                        QueueLimit        = 0
                    }));
        });

        // ── MVC com Autorização Global ─────────────────────────────────────
        builder.Services.AddControllersWithViews(opcoes =>
        {
            var politica = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            opcoes.Filters.Add(new AuthorizeFilter(politica));
        });

        // ── Razor Pages ────────────────────────────────────────────────────
        builder.Services.AddRazorPages(opcoes =>
        {
            opcoes.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
        });

        builder.Services.AddHttpClient<TrelloService>();

        // ── Cultura pt-BR ──────────────────────────────────────────────────
        var culturaInfo = new System.Globalization.CultureInfo("pt-BR");
        var opcoesLocalizacao = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(culturaInfo),
            SupportedCultures     = new[] { culturaInfo },
            SupportedUICultures   = new[] { culturaInfo }
        };

        // ── Porta para Square Cloud ───────────────────────────────────────
        if (Environment.GetEnvironmentVariable("SQUARE_CLOUD") == "true")
        {
            builder.WebHost.UseUrls("http://0.0.0.0:80");
        }

        var app = builder.Build();

        // ── Pipeline HTTP ──────────────────────────────────────────────────

        // 1º de tudo: respeitar headers do proxy do Square Cloud
        app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Erro");
            app.UseStatusCodePagesWithReExecute("/Erro", "?codigo={0}");
        }

        // HttpsRedirection só em DEV — em produção o Square Cloud termina HTTPS
        if (app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseRequestLocalization(opcoesLocalizacao);

        // ── Security Headers ───────────────────────────────────────────────
        app.Use(async (contexto, proximo) =>
        {
            var headers = contexto.Response.Headers;
            headers["X-Frame-Options"]        = "SAMEORIGIN";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"]        = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"]     = "camera=(), microphone=(), geolocation=()";

            if (!app.Environment.IsDevelopment())
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net; " +
                "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net; " +
                "font-src 'self' data: https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net https://unpkg.com; " +
                "img-src 'self' data: blob: https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "connect-src 'self' ws: wss: https://cdn.datatables.net https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "frame-ancestors 'self'; base-uri 'self'; form-action 'self';";

            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            await proximo(contexto);
        });

        app.UseStaticFiles();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMiddleware<DigDog.Middlewares.AssinaturaMiddleware>();

        // ── Rotas (uma rota só pra controllers) ────────────────────────────
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
            .RequireRateLimiting("geral");

        app.MapHub<DigDog.Hubs.KanbanHub>("/kanbanHub")
            .RequireRateLimiting("publica");

        app.MapHub<DigDog.Hubs.AssinaturaHub>("/assinaturaHub")
            .RequireRateLimiting("geral");

        // Razor Pages com limite "geral" — login específico pode ser apertado depois
        app.MapRazorPages()
            .RequireRateLimiting("geral");

        using (var escopo = app.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<Contexto>();
            db.Database.Migrate();
        }

        app.Run();
    }
}