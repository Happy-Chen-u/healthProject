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

        // POST: Account/Login 登入
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
                var user = await ValidateUserAsync(model.Username, model.Password);
                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "帳號或密碼錯誤");
                    return View(model);
                }

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
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12),
                    IsPersistent = true
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties
                );

                _logger.LogInformation($"使用者 {user.Username} 登入成功");

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

        // POST: Account/Logout 登出
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation($"使用者 {User.Identity.Name} 登出");
            return RedirectToAction("Index", "Home");
        }

        // GET: Account/ChangePassword 改密碼
        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        // POST: Account/ChangePassword 改密碼
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
                var username = User.Identity.Name;

                var success = await ChangeUserPasswordAsync(int.Parse(userId), model.OldPassword, model.NewPassword);

                if (success)
                {
                    _logger.LogInformation($"使用者 {username} 密碼變更成功");

                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    TempData["SuccessMessage"] = "密碼變更成功!請使用新密碼重新登入。";
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

        // GET: Account/ForgotPassword 忘記密碼
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // POST: Account/ForgotPassword 忘記密碼
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var query = "SELECT * FROM public.\"Users\" WHERE \"IDNumber\" = @IDNumber AND \"FullName\" = @FullName AND \"PhoneNumber\" = @PhoneNumber";
                using (var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@IDNumber", model.IDNumber);
                        cmd.Parameters.AddWithValue("@FullName", model.FullName);
                        cmd.Parameters.AddWithValue("@PhoneNumber", model.PhoneNumber);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                TempData["ResetUser"] = model.IDNumber;
                                return RedirectToAction("ResetPassword");
                            }
                            else
                            {
                                ModelState.AddModelError(string.Empty, "查無此使用者，請確認資料是否正確");
                                return View(model);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "忘記密碼處理失敗");
                ModelState.AddModelError(string.Empty, "系統發生錯誤，請稍後再試");
                return View(model);
            }
        }

        // GET: Account/ResetPassword 重設密碼
        [HttpGet]
        public IActionResult ResetPassword()
        {
            var idNumber = TempData["ResetUser"] as string;
            if (string.IsNullOrEmpty(idNumber))
            {
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.IDNumber = idNumber;
            return View();
        }

        // POST: Account/ResetPassword 重設密碼
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string idNumber, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword))
            {
                ModelState.AddModelError(string.Empty, "密碼不能為空");
                return View();
            }

            try
            {
                var query = "UPDATE public.\"Users\" SET \"PasswordHash\" = @PasswordHash WHERE \"IDNumber\" = @IDNumber";
                using (var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@PasswordHash", newPassword);
                        cmd.Parameters.AddWithValue("@IDNumber", idNumber);
                        var rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                        {
                            ModelState.AddModelError(string.Empty, "找不到對應的使用者，密碼未更新");
                            return View();
                        }
                    }
                }

                TempData["SuccessMessage"] = "密碼已成功重設，請使用新密碼登入";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重設密碼失敗");
                ModelState.AddModelError(string.Empty, "系統發生錯誤，請稍後再試");
                return View();
            }
        }


        // =========================
        // 資料庫操作
        // =========================
        private async Task<UserDBModel> ValidateUserAsync(string username, string password)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                _logger.LogInformation($"嘗試驗證使用者: {username}");

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

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

                    if (VerifyPassword(password, user.PasswordHash))
                    {
                        return user;
                    }
                }
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

                var checkQuery = @"SELECT ""PasswordHash"" FROM ""Users"" WHERE ""Id"" = @Id";
                await using var checkCommand = new NpgsqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@Id", userId);

                var currentHash = (string)await checkCommand.ExecuteScalarAsync();

                if (string.IsNullOrEmpty(currentHash) || !VerifyPassword(oldPassword, currentHash))
                {
                    return false;
                }

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

        // 明文比對 (開發測試)
        private bool VerifyPassword(string password, string passwordHash)
        {
            return password == passwordHash;
        }

        private string HashPassword(string password)
        {
            return password;
        }
    }
}
