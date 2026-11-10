using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickVault.Api.Models;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace QuickVault.Api.Controllers
{
    public enum LoanStatusFilter
    {
        PendingAssessment,
        PendingAdminReview,
        PendingDebiCheck,
        Approved,
        Rejected,
        Active,
        Settled,
        Defaulted
    }


    [Route("api/admin")]
    [ApiController]
    [Authorize]
    public class AdminController : ControllerBase
    {
        private readonly QuickVaultDbContext _context;

        public AdminController(QuickVaultDbContext context)
        {
            _context = context;
        }

        // 1. Get List of All Customers with Details
        [HttpGet("customers")]
        public async Task<IActionResult> GetAllCustomers()
        {
            var customers = await _context.Customers
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(new { count = customers.Count, customers });
        }

        // 2. Search Customer Details by 13-Digit SA ID Number
        [HttpGet("customers/search/{idNumber}")]
        public async Task<IActionResult> GetCustomerByIdNumber(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber) || idNumber.Length != 13)
            {
                return BadRequest(new { message = "Invalid ID number format. Must be 13 digits." });
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.IDNumber == idNumber);

            if (customer == null)
            {
                return NotFound(new { message = $"No customer found with ID number: {idNumber}" });
            }

            // Fetch associated loans for this customer
            var loans = await _context.Loans
                .Where(l => l.CustomerID == customer.CustomerID)
                .ToListAsync();

            return Ok(new { customer, loansCount = loans.Count, loans });
        }

        // 3. Get List of All Loans (with optional status dropdown filter)
        [HttpGet("loans")]
        public async Task<IActionResult> GetAllLoans([FromQuery] LoanStatusFilter? status)
        {
            var query = _context.Loans.AsQueryable();

            if (status.HasValue)
            {
                string statusStr = status.Value.ToString();
                query = query.Where(l => l.LoanStatus.ToLower() == statusStr.ToLower());
            }

            var loans = await query
                .Join(_context.Customers,
                    loan => loan.CustomerID,
                    cust => cust.CustomerID,
                    (loan, cust) => new
                    {
                        loan.LoanID,
                        cust.CustomerID,
                        cust.IDNumber,
                        CustomerName = $"{cust.FirstName} {cust.Surname}",
                        cust.MobileNumber,
                        loan.PrincipalAmount,
                        loan.TermMonths,
                        loan.TotalRepayment,
                        loan.BalanceRemaining,
                        loan.LoanStatus,
                        loan.DebiCheckStatus,
                        loan.AdminNotes,
                        loan.CreatedAt
                    })
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return Ok(new { filterApplied = status?.ToString() ?? "All", count = loans.Count, loans });
        }

        // 4. Search Specific Loan by LoanID
        [HttpGet("loans/{loanId:int}")]
        public async Task<IActionResult> GetLoanById(int loanId)
        {
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanID == loanId);

            if (loan == null)
            {
                return NotFound(new { message = $"Loan with ID #{loanId} not found." });
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.CustomerID == loan.CustomerID);

            var assessment = await _context.AffordabilityAssessments
                .FirstOrDefaultAsync(a => a.CustomerID == loan.CustomerID);

            return Ok(new
            {
                loan,
                customer,
                affordabilityAssessment = assessment
            });
        }

        // 5. Formal Admin Decision Endpoint (Approve / Reject)
        [HttpPost("loans/{loanId:int}/decision")]
        public async Task<IActionResult> MakeLoanDecision(int loanId, [FromBody] AdminDecisionDto decision)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var loan = await _context.Loans.FindAsync(loanId);
            if (loan == null)
            {
                return NotFound(new { message = $"Loan application #{loanId} not found." });
            }

            if (decision.IsApproved)
            {
                loan.LoanStatus = "Approved";
                loan.DebiCheckStatus = "Pending TT3";
                loan.AdminNotes = decision.Notes;
            }
            else
            {
                loan.LoanStatus = "Rejected";
                loan.AdminNotes = string.IsNullOrEmpty(decision.Notes) ? "Declined during manual compliance review." : decision.Notes;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Loan #{loan.LoanID} updated successfully.",
                loanId = loan.LoanID,
                status = loan.LoanStatus,
                adminNotes = loan.AdminNotes
            });
        }
    }

    public class AdminDecisionDto
    {
        [Required]
        public bool IsApproved { get; set; }
        
        [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
        public string Notes { get; set; } = string.Empty;
    }
}