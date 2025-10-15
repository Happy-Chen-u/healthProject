using healthProject.Models;

using Microsoft.AspNetCore.Authentication;

using Microsoft.AspNetCore.Authentication.Cookies;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.Data.SqlClient;

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

            // 如果已經登入，導向 Dashboard

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

                    new Claim(ClaimTypes.Role, user.Role) // 使用 Role 欄位

                };

                // 如果有身分證字號，也加入 Claim

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

                ModelState.AddModelError(string.Empty, "登入過程發生錯誤，請稍後再試");

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

                var success = await ChangeUserPasswordAsync(int.Parse(userId), model.OldPassword, model.NewPassword);

                if (success)

                {

                    TempData["SuccessMessage"] = "密碼變更成功";

                    return RedirectToAction("Dashboard", "Home");

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

        // 驗證使用者（從資料庫查詢）

        private async Task<UserDBModel> ValidateUserAsync(string username, string password)

        {

            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var connection = new SqlConnection(connectionString))

            {

                await connection.OpenAsync();

                // 查詢使用者 - 使用新的欄位名稱

                var query = @"

                    SELECT Id, Username, PasswordHash, FullName, Role, IDNumber, IsActive 

                    FROM Users 

                    WHERE Username = @Username AND IsActive = 1";

                using (var command = new SqlCommand(query, connection))

                {

                    command.Parameters.AddWithValue("@Username", username);

                    using (var reader = await command.ExecuteReaderAsync())

                    {

                        if (await reader.ReadAsync())

                        {

                            var user = new UserDBModel

                            {

                                Id = reader.GetInt32(0),

                                Username = reader.GetString(1),

                                PasswordHash = reader.GetString(2),

                                FullName = reader.IsDBNull(3) ? null : reader.GetString(3),

                                Role = reader.IsDBNull(4) ? "Patient" : reader.GetString(4),

                                IDNumber = reader.IsDBNull(5) ? null : reader.GetString(5),

                                IsActive = reader.GetBoolean(6)

                            };

                            // 驗證密碼

                            if (VerifyPassword(password, user.PasswordHash))

                            {

                                return user;

                            }

                        }

                    }

                }

            }

            return null;

        }

        // 變更密碼

        private async Task<bool> ChangeUserPasswordAsync(int userId, string oldPassword, string newPassword)

        {

            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var connection = new SqlConnection(connectionString))

            {

                await connection.OpenAsync();

                // 先驗證舊密碼 - 使用 Id 欄位

                var checkQuery = "SELECT PasswordHash FROM Users WHERE Id = @Id";

                string currentHash;

                using (var command = new SqlCommand(checkQuery, connection))

                {

                    command.Parameters.AddWithValue("@Id", userId);

                    currentHash = (string)await command.ExecuteScalarAsync();

                }

                if (!VerifyPassword(oldPassword, currentHash))

                {

                    return false;

                }

                // 更新新密碼 - 使用 Id 欄位

                var updateQuery = "UPDATE Users SET PasswordHash = @NewPasswordHash WHERE Id = @Id";

                using (var command = new SqlCommand(updateQuery, connection))

                {

                    command.Parameters.AddWithValue("@Id", userId);

                    command.Parameters.AddWithValue("@NewPasswordHash", HashPassword(newPassword));

                    await command.ExecuteNonQueryAsync();

                    return true;

                }

            }

        }

        // 密碼驗證（請根據您的加密方式調整）

        private bool VerifyPassword(string password, string passwordHash)

        {

            // 方案1：如果使用 BCrypt

            // return BCrypt.Net.BCrypt.Verify(password, passwordHash);

            // 方案2：如果密碼是明文（不建議，僅供開發測試）

            return password == passwordHash;

            // 方案3：如果使用其他 Hash 方式，請自行實作

        }

        // 密碼加密（請根據您的加密方式調整）

        private string HashPassword(string password)

        {

            // 方案1：如果使用 BCrypt

            // return BCrypt.Net.BCrypt.HashPassword(password);

            // 方案2：如果密碼是明文（不建議，僅供開發測試）

            return password;

            // 方案3：如果使用其他 Hash 方式，請自行實作

        }

    }

}
