using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickVault.Api.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace QuickVault.Api.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly QuickVaultDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(QuickVaultDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("admin-login")]
        public IActionResult AdminLogin([FromBody] AdminLoginDto login)
        {
            // Hardcoded credentials for local testing
            if (login.Username == "admin" && login.Password == "QuickVaultAdmin2026!")
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_configuration["JwtSettings:Secret"]!);
                
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[] { 
                        new Claim(ClaimTypes.Name, "AdminUser"), 
                        new Claim(ClaimTypes.Role, "Admin") 
                    }),
                    Expires = DateTime.UtcNow.AddHours(2),
                    Issuer = _configuration["JwtSettings:Issuer"],
                    Audience = _configuration["JwtSettings:Audience"],
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };
                
                var token = tokenHandler.CreateToken(tokenDescriptor);
                return Ok(new { token = tokenHandler.WriteToken(token) });
            }

            return Unauthorized(new { message = "Invalid admin username or password." });
        }

        // 1. Request OTP using ID Number
        [HttpPost("request-otp")]
        public async Task<IActionResult> RequestOtp([FromBody] OtpRequestDto request)
        {
            // Verify if customer exists in database
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.IDNumber == request.IDNumber);

            if (customer == null)
            {
                return NotFound(new { message = "No customer found with this ID number. Please register first." });
            }

            // Generate a random 6-digit OTP code
            string otpCode = new Random().Next(100000, 999999).ToString();

            // Save OTP session with 5-minute expiry
            var otpSession = new OTPSession
            {
                IDNumber = customer.IDNumber,
                OTPCode = otpCode,
                ExpiresAt = DateTime.Now.AddMinutes(5),
                IsUsed = false
            };

            _context.OTPSessions.Add(otpSession);
            await _context.SaveChangesAsync();

            // TODO: Integrate SMS (Twilio/Clickatell) or Email gateway based on customer.CommunicationPreference
            // For development testing, we return the OTP in the response body so you can view it easily in Postman/Swagger
            return Ok(new { 
                message = $"OTP successfully sent via {customer.CommunicationPreference}", 
                debugOtp = otpCode // Remove in production!
            });
        }

        // 2. Verify OTP and Authenticate
        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyDto request)
        {
            var validSession = await _context.OTPSessions
                .Where(s => s.IDNumber == request.IDNumber && s.OTPCode == request.OTPCode && !s.IsUsed && s.ExpiresAt > DateTime.Now)
                .OrderByDescending(s => s.OTPID)
                .FirstOrDefaultAsync();

            if (validSession == null)
            {
                return BadRequest(new { message = "Invalid or expired OTP code." });
            }

            // Mark OTP as used
            validSession.IsUsed = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Authentication successful. Welcome to QuickVault.", token = "mock-jwt-token-quickvault" });
        }
    }

    public class OtpRequestDto
    {
        public string IDNumber { get; set; } = string.Empty;
    }

    public class OtpVerifyDto
    {
        public string IDNumber { get; set; } = string.Empty;
        public string OTPCode { get; set; } = string.Empty;
    }

    public class AdminLoginDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}