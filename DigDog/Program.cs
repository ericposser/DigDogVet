using DigDog.Data;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
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
        // Pool de conexões explícito: evita que picos de usuários simultâneos
        // esgotem o limite de conexões do MySQL (padrão do servidor: 151).
        // MaximumPoolSize=50 é conservador; ajustar conforme o servidor.
        var connectionString = (builder.Configuration.GetConnectionString("DefaultConnection") ?? "")
            + ";MaximumPoolSize=50;MinimumPoolSize=5;ConnectionTimeout=30;";

        builder.Services.AddDbContext<Contexto>(opcoes =>
            opcoes.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        // Factory necessária para o HomeController criar instâncias independentes
        // de DbContext nas queries paralelas via Task.WhenAll.
        builder.Services.AddDbContextFactory<Contexto>(opcoes =>
            opcoes.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)),
            ServiceLifetime.Scoped);

        builder.Services.AddDataProtection();
        builder.Services.AddSignalR();

        // Cache em memória — usado pelo PermissaoService para evitar 3 queries
        // ao banco em cada action protegida por [RequerPermissao].
        builder.Services.AddMemoryCache();

        builder.Services.AddScoped<PermissaoService>();
        builder.Services.AddScoped<LogService>();
        builder.Services.AddHostedService<LimpezaLogService>();
        builder.Services.AddScoped<CaktoWebhookService>();

        // ── Identity ───────────────────────────────────────────────────────
        builder.Services.AddDefaultIdentity<IdentityUser>(opcoes =>
        {
            opcoes.Password.RequireDigit           = false;
            opcoes.Password.RequiredLength         = 3;
            opcoes.Password.RequireUppercase       = false;
            opcoes.Password.RequireNonAlphanumeric = false;
            opcoes.Password.RequireLowercase       = false;
            opcoes.SignIn.RequireConfirmedAccount  = false;

            // Bloqueio de conta após tentativas falhas de login
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

            // Hardening do cookie
            opcoes.Cookie.HttpOnly     = true;
            opcoes.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            opcoes.Cookie.SameSite     = SameSiteMode.Strict;
            opcoes.Cookie.Name         = "__Host-DigDog"; // __Host força Secure + sem Domain + Path=/
            opcoes.ExpireTimeSpan      = TimeSpan.FromHours(8);
            opcoes.SlidingExpiration   = true;

            // SecurityStampValidator verifica se o usuário ainda existe/é válido,
            // mas só vai ao banco 1x a cada 30 minutos (configurado abaixo).
            // Substitui o OnValidatePrincipal manual que fazia SELECT em toda requisição.
            opcoes.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
        });

        // ValidationInterval pertence ao SecurityStampValidatorOptions, não ao cookie.
        // Controla de quanto em quanto tempo o Identity valida o stamp no banco.
        builder.Services.Configure<SecurityStampValidatorOptions>(opcoes =>
        {
            opcoes.ValidationInterval = TimeSpan.FromMinutes(30);
        });

        // ── Rate Limiting ──────────────────────────────────────────────────
        // "login"   → Razor Pages de autenticação: 5 tentativas / 5 min por IP
        // "publica" → Rotas anônimas (carteirinha, kanban): 30 req/min por IP
        // "webhook" → Endpoints externos (Cakto): 60 req/min por IP
        // "geral"   → Todas as rotas autenticadas: 120 req/min por IP
        // GlobalLimiter → barreira absoluta: 200 req/min por IP em qualquer rota

        builder.Services.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // ── Login / Registro ───────────────────────────────────────────
            opcoes.AddFixedWindowLimiter("login", limiter =>
            {
                limiter.Window               = TimeSpan.FromMinutes(5);
                limiter.PermitLimit          = 5;
                limiter.QueueLimit           = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // ── Rotas públicas anônimas ────────────────────────────────────
            opcoes.AddSlidingWindowLimiter("publica", limiter =>
            {
                limiter.Window               = TimeSpan.FromMinutes(1);
                limiter.PermitLimit          = 30;
                limiter.SegmentsPerWindow    = 6;
                limiter.QueueLimit           = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // ── Webhooks / API externa ─────────────────────────────────────
            opcoes.AddFixedWindowLimiter("webhook", limiter =>
            {
                limiter.Window               = TimeSpan.FromMinutes(1);
                limiter.PermitLimit          = 60;
                limiter.QueueLimit           = 5;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // ── Rotas autenticadas ─────────────────────────────────────────
            opcoes.AddSlidingWindowLimiter("geral", limiter =>
            {
                limiter.Window               = TimeSpan.FromMinutes(1);
                limiter.PermitLimit          = 120;
                limiter.SegmentsPerWindow    = 6;
                limiter.QueueLimit           = 10;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // ── Barreira global por IP ─────────────────────────────────────
            opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        Window               = TimeSpan.FromMinutes(1),
                        PermitLimit          = 200,
                        SegmentsPerWindow    = 6,
                        QueueLimit           = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
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

        // ── Razor Pages (libera as páginas de login/registro) ──────────────
        builder.Services.AddRazorPages(opcoes =>
        {
            opcoes.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
        });

        builder.Services.AddHttpClient<TrelloService>();

        var app = builder.Build();

        // ── Pipeline HTTP ──────────────────────────────────────────────────
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Erro");
            app.UseStatusCodePagesWithReExecute("/Erro/{0}");
        }

        app.UseHttpsRedirection();

        // ── Security Headers ───────────────────────────────────────────────
        // Antes de UseStaticFiles para cobrir também arquivos estáticos.
        app.Use(async (contexto, proximo) =>
        {
            var headers = contexto.Response.Headers;

            // Anti-clickjacking
            headers["X-Frame-Options"] = "SAMEORIGIN";

            // Anti-MIME sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Não vaza path/query em requisições cross-origin
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // HSTS — apenas em produção para não quebrar o dev local
            if (!app.Environment.IsDevelopment())
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            // Desabilita acesso a câmera, microfone e geolocalização
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // CSP: lista branca das CDNs do projeto + ws/wss para SignalR
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net; " +
                "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net; " +
                "font-src 'self' data: https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net https://unpkg.com; " +
                "img-src 'self' data: blob: https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "connect-src 'self' ws: wss: https://cdn.datatables.net https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "frame-ancestors 'self'; " +
                "base-uri 'self'; " +
                "form-action 'self';";

            // Remove headers que revelam a stack do servidor
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            await proximo(contexto);
        });

        app.UseStaticFiles();
        app.UseRouting();

        // Rate Limiter antes de Authentication para bloquear antes de processar identidade
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        // Middleware de assinatura: verifica validade com cache em claim (30 min),
        // evitando 2 queries ao banco em cada requisição autenticada.
        app.UseMiddleware<DigDog.Middlewares.AssinaturaMiddleware>();

        // ── Rotas ──────────────────────────────────────────────────────────
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
            .RequireRateLimiting("geral");

        app.MapControllerRoute(
            name: "publico",
            pattern: "{controller}/{action=Index}/{id?}")
            .RequireRateLimiting("publica")
            .WithMetadata(new AllowAnonymousAttribute());

        app.MapHub<DigDog.Hubs.KanbanHub>("/kanbanHub")
            .RequireRateLimiting("publica");

        app.MapHub<DigDog.Hubs.AssinaturaHub>("/assinaturaHub")
            .RequireRateLimiting("geral");

        app.MapRazorPages()
            .RequireRateLimiting("login");

        // ── Porta para produção (Square Cloud) ────────────────────────────
        if (!app.Environment.IsDevelopment())
        {
            app.Urls.Add("http://0.0.0.0:80");
        }
        
        using (var escopo = app.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<Contexto>();
            db.Database.Migrate();
        }
        
        app.Run();
    }
}
