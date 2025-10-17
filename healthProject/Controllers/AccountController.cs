using healthProject.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;
using System.Text;

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

                // 檢查是否第一次登入
                if (user.IsFirstLogin)
                {
                    // 產生 OTP
                    string otp = GenerateOtp();
                    // 儲存到 Session，時效 5 分鐘
                    HttpContext.Session.SetString("Otp", otp);
                    HttpContext.Session.SetInt32("UserIdForOtp", user.Id);
                    HttpContext.Session.SetString("OtpExpiry", DateTime.Now.AddMinutes(5).ToString("o"));

                    // 發送 OTP 到 LINE
                    await SendOtpToLineAsync(user.LineUserId, otp);

                    // 跳轉到 OTP 驗證頁面，不 SignIn
                    return RedirectToAction("VerifyOtp");
                }

                // 非第一次登入，進行 SignIn
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

        // 新增: GET VerifyOtp
        [HttpGet]
        public IActionResult VerifyOtp()
        {
            // 不需要 Authorize，因為還沒 SignIn
            return View();
        }

        // POST: VerifyOtp (修正版)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(string otp)
        {
            var storedOtp = HttpContext.Session.GetString("Otp");
            var expiryStr = HttpContext.Session.GetString("OtpExpiry");
            var userId = HttpContext.Session.GetInt32("UserIdForOtp");

            if (string.IsNullOrEmpty(storedOtp) || string.IsNullOrEmpty(expiryStr) || !userId.HasValue)
            {
                ModelState.AddModelError("", "OTP 會話無效，請重新登入");
                return View();
            }

            var expiry = DateTime.Parse(expiryStr);
            if (DateTime.Now > expiry)
            {
                // 清除過期的 Session
                HttpContext.Session.Remove("Otp");
                HttpContext.Session.Remove("UserIdForOtp");
                HttpContext.Session.Remove("OtpExpiry");

                ModelState.AddModelError("", "OTP 已過期，請重新登入");
                return View();
            }

            if (otp != storedOtp)
            {
                ModelState.AddModelError("", "OTP 錯誤，請重新輸入");
                return View();
            }

            // OTP 驗證成功
            try
            {
                // 更新資料庫：設定為非第一次登入
                await UpdateFirstLoginAsync(userId.Value);

                // 取得使用者資料 (不需要密碼)
                var user = await GetUserByIdAsync(userId.Value);

                if (user == null)
                {
                    ModelState.AddModelError("", "使用者資料異常，請聯絡管理員");
                    return View();
                }

                // 清除 OTP Session
                HttpContext.Session.Remove("Otp");
                HttpContext.Session.Remove("UserIdForOtp");
                HttpContext.Session.Remove("OtpExpiry");

                // 執行登入
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

                _logger.LogInformation($"使用者 {user.Username} 通過 OTP 驗證並登入");

                // 導向變更密碼頁面
                TempData["SuccessMessage"] = "驗證成功!請立即變更您的密碼";
                return RedirectToAction("ChangePassword");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OTP 驗證後處理失敗");
                ModelState.AddModelError("", "系統發生錯誤，請稍後再試");
                return View();
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            _logger.LogInformation($"Received ForgotPassword request - IDNumber: {model.IDNumber}, FullName: {model.FullName}, PhoneNumber: {model.PhoneNumber}");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState is invalid in ForgotPassword");
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
                                _logger.LogInformation($"User found with IDNumber: {model.IDNumber}, redirecting to ResetPassword");
                                TempData["ResetUser"] = model.IDNumber;
                                return RedirectToAction("ResetPassword");
                            }
                            else
                            {
                                _logger.LogWarning($"No user found for IDNumber: {model.IDNumber}");
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

        [HttpGet]
        public IActionResult ResetPassword()
        {
            var idNumber = TempData["ResetUser"] as string;
            _logger.LogInformation($"ResetPassword called with TempData ResetUser: {idNumber}");

            if (string.IsNullOrEmpty(idNumber))
            {
                _logger.LogWarning("TempData ResetUser is null or empty, redirecting to ForgotPassword");
                return RedirectToAction("ForgotPassword");
            }

            var model = new ResetPasswordViewModel
            {
                IDNumber = idNumber
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var query = "UPDATE public.\"Users\" SET \"PasswordHash\" = @PasswordHash WHERE \"IDNumber\" = @IDNumber";
                using (var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@PasswordHash", model.Password);
                        cmd.Parameters.AddWithValue("@IDNumber", model.IDNumber);
                        var rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                        {
                            ModelState.AddModelError(string.Empty, "找不到對應的使用者，密碼未更新");
                            return View(model);
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
                return View(model);
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
                    SELECT ""Id"", ""Username"", ""PasswordHash"", ""FullName"", ""Role"", ""IDNumber"", ""IsActive"", ""IsFirstLogin"", ""LineUserId""
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
                        IsActive = reader.GetBoolean(6),
                        IsFirstLogin = reader.GetBoolean(7),
                        LineUserId = reader.IsDBNull(8) ? null : reader.GetString(8)
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


        // 新增: 直接透過 ID 取得使用者資料
        private async Task<UserDBModel> GetUserByIdAsync(int userId)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                var query = @"
            SELECT ""Id"", ""Username"", ""PasswordHash"", ""FullName"", ""Role"", ""IDNumber"", 
                   ""IsActive"", ""IsFirstLogin"", ""LineUserId""
            FROM ""Users""
            WHERE ""Id"" = @Id AND ""IsActive"" = true
            LIMIT 1";

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", userId);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new UserDBModel
                    {
                        Id = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        PasswordHash = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        FullName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Role = reader.IsDBNull(4) ? "Patient" : reader.GetString(4),
                        IDNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
                        IsActive = reader.GetBoolean(6),
                        IsFirstLogin = reader.GetBoolean(7),
                        LineUserId = reader.IsDBNull(8) ? null : reader.GetString(8)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"取得使用者資料時發生錯誤: {ex.Message}");
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

        private string GenerateOtp()
        {
            Random random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private async Task SendOtpToLineAsync(string lineUserId, string otp)
        {
            if (string.IsNullOrEmpty(lineUserId))
            {
                throw new Exception("無 LINE User ID，無法發送 OTP");
            }

            var channelAccessToken = _configuration["Line:ChannelAccessToken"];

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {channelAccessToken}");

            var message = new
            {
                to = lineUserId,
                messages = new[]
                {
                    new { type = "text", text = $"您的 OTP 驗證碼是：{otp}。請在 5 分鐘內輸入。" }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.line.me/v2/bot/message/push", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"發送 LINE OTP 失敗: {error}");
                throw new Exception("發送 OTP 失敗");
            }
        }

        private async Task UpdateFirstLoginAsync(int userId)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"UPDATE ""Users"" SET ""IsFirstLogin"" = false WHERE ""Id"" = @Id";
            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", userId);
            await command.ExecuteNonQueryAsync();
        }

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
