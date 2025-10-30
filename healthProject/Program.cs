using Microsoft.AspNetCore.Authentication.Cookies;
using Hangfire;
using Hangfire.PostgreSql;
using healthProject.Services;
using Hangfire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// �[�J�O����֨� (Session �ݭn)
builder.Services.AddDistributedMemoryCache();

// �[�J Session �䴩
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// �[�J���ҪA��
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
// ?? ���U���d���R�����A��
// ========================================
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ScheduledJobService>();

// ========================================
// ?? �[�J Hangfire (�g���Ƶ{)
// ========================================
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )));

// �[�J Hangfire �I���u�@�A��
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

// ���n: Session �n�b Routing ����, Authentication ���e
app.UseSession();

// �ϥ����ҩM���v
app.UseAuthentication();
app.UseAuthorization();

// ========================================
// ?? �ҥ� Hangfire Dashboard (�Ⱥ޲z���i�d��)
// ========================================
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "�N�¯g�Ըs�޲z�t�� - �Ƶ{�ʱ�"
});

// ========================================
// ?? �]�w�C�g��ߤW 8 �I�o�e�g��
// ========================================
RecurringJob.AddOrUpdate<ScheduledJobService>(
    "send-weekly-reports",
    service => service.SendWeeklyReportsAsync(),
    "0 20 * * 0", // Cron ��F��: �C�g�� 20:00
    TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ========================================
// ?? Hangfire ���v�L�o�� (�Ⱥ޲z���i�d�� Dashboard)
// ========================================
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // ���\���a�}�o���ҳX��
        if (httpContext.Request.Host.Host == "localhost")
        {
            return true;
        }

        // �Ͳ����һݭn�޲z���v��
        return httpContext.User.Identity?.IsAuthenticated == true &&
               httpContext.User.IsInRole("Admin");
    }
}