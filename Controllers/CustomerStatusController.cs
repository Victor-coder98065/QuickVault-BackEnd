using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickVault.Api.Models;
using System.Linq;
using System.Threading.Tasks;

namespace QuickVault.Api.Controllers
{
    [Route("api/customer")]
    [ApiController]
    public class CustomerStatusController : ControllerBase
    {
        private readonly QuickVaultDbContext _context;

        public CustomerStatusController(QuickVaultDbContext context)
        {
            _context = context;
        }

        // Get customer profile, active loan history, and balance remaining
        [HttpGet("status/{idNumber}")]
        public async Task<IActionResult> GetCustomerStatus(string idNumber)
        {
            // 1. Find customer by ID number
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.IDNumber == idNumber);

            if (customer == null)
            {
                return NotFound(new { message = "No customer record found for this ID number." });
            }

            // 2. Fetch all loans associated with this customer
            var loans = await _context.Loans
                .Where(l => l.CustomerID == customer.CustomerID)
                .OrderByDescending(l => l.LoanID)
                .ToListAsync();

            // 3. Fetch latest affordability assessment
            var latestAssessment = await _context.AffordabilityAssessments
                .Where(a => a.CustomerID == customer.CustomerID)
                .OrderByDescending(a => a.AssessmentID)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                message = "Customer status retrieved successfully.",
                customerProfile = new
                {
                    customer.CustomerID,
                    customer.IDNumber,
                    customer.FirstName,
                    customer.Surname,
                    customer.MobileNumber,
                    customer.EmailAddress,
                    customer.CommunicationPreference
                },
                loans = loans,
                affordabilityAssessment = latestAssessment
            });
        }
    }
}