using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ABCRetails.Models;
using ABCRetails.Models.ViewModels;
using ABCRetails.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace ABCRetails.Controllers
{
    public class LoginController : Controller
    {
        private readonly AuthDbContext _context;
        private readonly ILogger<LoginController> _logger;

        public LoginController(AuthDbContext context, ILogger<LoginController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == model.Username);

                if (user == null)
                {
                    ModelState.AddModelError("", "Invalid username or password.");
                    return View(model);
                }

                // Verify password
                if (VerifyPassword(model.Password, user.PasswordHash))
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim(ClaimTypes.Role, user.Role)
                    };

                    var claimsIdentity = new ClaimsIdentity(
                        claims, CookieAuthenticationDefaults.AuthenticationScheme);

                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity),
                        authProperties);

                    _logger.LogInformation("User {Username} logged in.", user.Username);

                    // Redirect based on role
                    if (user.Role == "Admin")
                    {
                        return RedirectToAction("AdminDashboard", "Home");
                    }
                    else
                    {
                        return RedirectToAction("CustomerDashboard", "Home");
                    }
                }

                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user {Username}", model.Username);
                ModelState.AddModelError("", "An error occurred during login. Please try again.");
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User logged out.");
            return RedirectToAction("Index", "Home");
        }

        private bool VerifyPassword(string password, string storedHash)
        {
            return HashPassword(password) == storedHash;
        }

        private string HashPassword(string password)
        {
            // Simple hashing for demonstration
            return password + "_hashed";
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            return View(new User());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(User user, string password)
        {
            // Remove this validation - password is not part of User model
            if (!ModelState.IsValid)
            {
                // Check only for required User fields
                if (string.IsNullOrWhiteSpace(user.Username) ||
                    string.IsNullOrWhiteSpace(user.Email) ||
                    string.IsNullOrWhiteSpace(password))
                {
                    ModelState.AddModelError("", "Username, Email and Password are required.");
                    return View(user);
                }
            }

            try
            {
                // Check if username already exists
                if (await _context.Users.AnyAsync(u => u.Username == user.Username))
                {
                    ModelState.AddModelError("Username", "Username already exists.");
                    return View(user);
                }

                // Check if email already exists
                if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                {
                    ModelState.AddModelError("Email", "Email already exists.");
                    return View(user);
                }

                // Force Customer role for new registrations (security measure)
                user.Role = "Customer";
                user.PasswordHash = HashPassword(password);
                user.CreatedAt = DateTime.UtcNow;

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation("New user registered: {Username}", user.Username);

                // Auto-login after registration
                var loginModel = new LoginViewModel
                {
                    Username = user.Username,
                    Password = password,
                    RememberMe = false
                };

                // Call login directly instead of redirecting to Index action
                return await LoginAfterRegistration(loginModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering user {Username}", user.Username);
                ModelState.AddModelError("", $"An error occurred during registration: {ex.Message}");
            }
            return View(user);
        }

        private async Task<IActionResult> LoginAfterRegistration(LoginViewModel model)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == model.Username);

                if (user != null && VerifyPassword(model.Password, user.PasswordHash))
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim(ClaimTypes.Role, user.Role)
                    };

                    var claimsIdentity = new ClaimsIdentity(
                        claims, CookieAuthenticationDefaults.AuthenticationScheme);

                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity),
                        authProperties);

                    _logger.LogInformation("User {Username} auto-logged in after registration.", user.Username);

                    // Redirect based on role
                    if (user.Role == "Admin")
                    {
                        return RedirectToAction("AdminDashboard", "Home");
                    }
                    else
                    {
                        return RedirectToAction("CustomerDashboard", "Home");
                    }
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto-login after registration for user {Username}", model.Username);
                return RedirectToAction("Index");
            }
        }
    }
}