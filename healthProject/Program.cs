using Microsoft.AspNetCore.Authentication.Cookies;
using Hangfire;
using Hangfire.PostgreSql;
using healthProject.Services;
using Hangfire.Dashboard;
using healthProject;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// 加入記憶體快取 (Session 需要)
builder.Services.AddDistributedMemoryCache();

// 加入 Session 支援
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// 加入驗證服務
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// ========================================
// ?? 註冊健康分析相關服務
// ========================================
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ScheduledJobService>();
builder.Services.AddHostedService<BackgroundJobService>();

// ========================================
// ?? 加入 Hangfire (週報排程)
// ========================================
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )));

// 加入 Hangfire 背景工作服務
builder.Services.AddHangfireServer();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// 重要: Session 要在 Routing 之後, Authentication 之前
app.UseSession();

// 使用驗證和授權
app.UseAuthentication();
app.UseAuthorization();

// ========================================
// 啟用 Hangfire Dashboard (僅管理員可查看)
// ========================================
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "代謝症候群管理系統 - 排程監控"
});

// ========================================
// 設定每週日晚上 8 點發送週報
// ========================================
RecurringJob.AddOrUpdate<ScheduledJobService>(
    "send-weekly-reports",
    service => service.SendWeeklyReportsAsync(),
    "0 20 * * 0", // Cron 表達式: 每週日 20:00
    TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();

// ========================================
// ?? Hangfire 授權過濾器 (僅管理員可查看 Dashboard)
// ========================================
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // 允許本地開發環境訪問
        if (httpContext.Request.Host.Host == "localhost")
        {
            return true;
        }

        // 生產環境需要管理員權限
        return httpContext.User.Identity?.IsAuthenticated == true &&
               httpContext.User.IsInRole("Admin");
    }
}