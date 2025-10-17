using healthProject.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace healthProject.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(IConfiguration configuration, ILogger<AccountController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // GET: Account/Login
        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Dashboard", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // 從資料庫驗證使用者
                var user = await ValidateUserAsync(model.Username, model.Password);

                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "帳號或密碼錯誤");
                    return View(model);
                }

                // 創建 Claims
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim("FullName", user.FullName ?? user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                };

                if (!string.IsNullOrEmpty(user.IDNumber))
                {
                    claims.Add(new Claim("IDNumber", user.IDNumber));
                }

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe
                        ? DateTimeOffset.UtcNow.AddDays(30)
                        : DateTimeOffset.UtcNow.AddHours(12)
                };

                // 登入
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                _logger.LogInformation($"使用者 {user.Username} 登入成功");

                // 導向指定頁面或 Dashboard
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Dashboard", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登入過程發生錯誤");
                ModelState.AddModelError(string.Empty, $"登入失敗：{ex.Message}");
                return View(model);
            }
        }

        // POST: Account/Logout
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation($"使用者 {User.Identity.Name} 登出");
            return RedirectToAction("Index", "Home");
        }

        // GET: Account/ChangePassword
        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        // POST: Account/ChangePassword
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var username = User.Identity.Name; // 保存使用者名稱用於 log

                var success = await ChangeUserPasswordAsync(int.Parse(userId), model.OldPassword, model.NewPassword);

                if (success)
                {
                    _logger.LogInformation($"使用者 {username} 密碼變更成功");

                    // 登出使用者
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    // 設定成功訊息
                    TempData["SuccessMessage"] = "密碼變更成功!請使用新密碼重新登入。";

                    // 導向登入頁面
                    return RedirectToAction("Login", "Account");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "舊密碼錯誤");
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "變更密碼時發生錯誤");
                ModelState.AddModelError(string.Empty, "密碼變更失敗，請稍後再試");
                return View(model);
            }
        }

        // ============================================
        // 資料庫查詢方法 (PostgreSQL)
        // ============================================

        private async Task<UserDBModel> ValidateUserAsync(string username, string password)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                _logger.LogInformation($"嘗試驗證使用者: {username}");

                // 使用 await using 確保連線正確釋放
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("資料庫連線成功");

                var query = @"
                    SELECT ""Id"", ""Username"", ""PasswordHash"", ""FullName"", ""Role"", ""IDNumber"", ""IsActive""
                    FROM ""Users""
                    WHERE ""Username"" = @Username AND ""IsActive"" = true
                    LIMIT 1";

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("@Username", username);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var user = new UserDBModel
                    {
                        Id = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        PasswordHash = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        FullName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Role = reader.IsDBNull(4) ? "Patient" : reader.GetString(4),
                        IDNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
                        IsActive = reader.GetBoolean(6)
                    };

                    _logger.LogInformation($"找到使用者: {user.Username}, Role: {user.Role}");

                    // 驗證密碼
                    if (VerifyPassword(password, user.PasswordHash))
                    {
                        _logger.LogInformation("密碼驗證成功");
                        return user;
                    }
                    else
                    {
                        _logger.LogWarning("密碼驗證失敗");
                    }
                }
                else
                {
                    _logger.LogWarning($"找不到使用者: {username}");
                }
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError(ex, $"PostgreSQL 錯誤: {ex.Message}");
                throw new Exception($"資料庫連線失敗: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"驗證使用者時發生錯誤: {ex.Message}");
                throw;
            }

            return null;
        }

        private async Task<bool> ChangeUserPasswordAsync(int userId, string oldPassword, string newPassword)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                // 先驗證舊密碼
                var checkQuery = @"SELECT ""PasswordHash"" FROM ""Users"" WHERE ""Id"" = @Id";

                await using var checkCommand = new NpgsqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@Id", userId);

                var currentHash = (string)await checkCommand.ExecuteScalarAsync();

                if (string.IsNullOrEmpty(currentHash) || !VerifyPassword(oldPassword, currentHash))
                {
                    return false;
                }

                // 更新新密碼
                var updateQuery = @"UPDATE ""Users"" SET ""PasswordHash"" = @NewPasswordHash WHERE ""Id"" = @Id";

                await using var updateCommand = new NpgsqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@Id", userId);
                updateCommand.Parameters.AddWithValue("@NewPasswordHash", HashPassword(newPassword));

                await updateCommand.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "變更密碼時發生資料庫錯誤");
                throw;
            }
        }

        // 密碼驗證（開發測試用 - 明文比對）
        private bool VerifyPassword(string password, string passwordHash)
        {
            // 開發測試階段使用明文比對
            return password == passwordHash;

            // 正式環境建議使用 BCrypt:
            // return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }

        // 密碼加密（開發測試用 - 明文儲存）
        private string HashPassword(string password)
        {
            // 開發測試階段直接回傳明文
            return password;

            // 正式環境建議使用 BCrypt:
            // return BCrypt.Net.BCrypt.HashPassword(password);
        }
    }
}