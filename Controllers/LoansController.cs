using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickVault.Api.Models;
using QuickVault.Api.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace QuickVault.Api.Controllers
{
    [Route("api/loans")]
    [ApiController]
    public class LoansController : ControllerBase
    {
        private readonly QuickVaultDbContext _context;
        private readonly LoanSettings _settings;
        private readonly IAffordabilityService _affordabilityService;

        public LoansController(
            QuickVaultDbContext context,
            IOptions<LoanSettings> settings,
            IAffordabilityService affordabilityService)
        {
            _context = context;
            _settings = settings.Value;
            _affordabilityService = affordabilityService;
        }

        [HttpPost("apply")]
        public async Task<IActionResult> ApplyForLoan([FromBody] LoanApplicationDto request)
        {
            // 1. Check if customer exists; if not, auto-register them
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.IDNumber == request.IDNumber);

            if (customer == null)
            {
                customer = new Customer
                {
                    IDNumber = request.IDNumber,
                    FirstName = "Applicant",
                    Surname = "User",
                    MobileNumber = "0820000000",
                    EmailAddress = $"{request.IDNumber}@quickvault.co.za",
                    CommunicationPreference = "SMS",
                    CustomerAccountStatus = "Active",
                    IsBlacklisted = false,
                    IsUnderDebtReview = false
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }

            // 2. Regulatory Check: Blacklist & Debt Review (Section 86 NCA)
            if (customer.IsBlacklisted || customer.IsUnderDebtReview)
            {
                var rejectedLoan = new Loan
                {
                    CustomerID = customer.CustomerID,
                    PrincipalAmount = request.PrincipalAmount,
                    TermMonths = request.TermMonths,
                    TotalRepayment = 0,
                    BalanceRemaining = 0,
                    LoanStatus = "Rejected",
                    DebiCheckStatus = "Pending Auth",
                    AdminNotes = customer.IsUnderDebtReview
        ? "Rejected instantly: Customer is under active debt review/counselling."
        : "Rejected instantly: Customer is blacklisted / adverse credit history."
                };
                _context.Loans.Add(rejectedLoan);
                await _context.SaveChangesAsync();

                await LogLoanHistoryAsync(
                    rejectedLoan.LoanID,
                    customer.IDNumber,
                    "Created",
                    "None",
                    rejectedLoan.LoanStatus,
                    rejectedLoan.TotalRepayment,
                    rejectedLoan.BalanceRemaining,
                    rejectedLoan.AdminNotes
                );

                return BadRequest(new
                {
                    message = "Loan application declined immediately.",
                    reason = rejectedLoan.AdminNotes
                });
            }

            // 3. Regulatory Check: Age Verification (Must be 18 or older)
            if (!ValidateSouthAfricanAge(request.IDNumber))
            {
                return BadRequest(new { message = "Loan declined: Applicant must be 18 years or older in accordance with South African law." });
            }

            // 4. Tiered Short-Term Interest Rates (Conforming to NCA short-term transaction guidelines up to 5% per month)
            decimal monthlyInterestRate = request.TermMonths switch
            {
                1 => 0.0300m, // 3.0% per month for 1 month
                2 => 0.0400m, // 4.0% per month for 2 months
                3 => 0.0500m, // 5.0% per month for 3 months (NCA short-term maximum cap)
                _ => 0.0500m
            };

            // 5. Perform NCA Affordability Assessment & Auto-Scaling Check with Mandatory Buffer
            var affordabilityResult = _affordabilityService.EvaluateAffordability(
                request.NetSalary,
                request.MonthlyExpenses,
                request.PrincipalAmount,
                request.TermMonths,
                monthlyInterestRate
            );

            if (!affordabilityResult.IsAffordable)
            {
                var declinedLoan = new Loan
                {
                    CustomerID = customer.CustomerID,
                    PrincipalAmount = request.PrincipalAmount,
                    TermMonths = request.TermMonths,
                    LoanStatus = "Rejected",
                    AdminNotes = affordabilityResult.Message
                };
                _context.Loans.Add(declinedLoan);
                await _context.SaveChangesAsync();

                return BadRequest(new
                {
                    message = "Loan declined: Affordability assessment failed.",
                    reason = affordabilityResult.Message,
                    disposableIncome = affordabilityResult.DisposableIncome
                });
            }

            // Use the approved principal amount (auto-scaled down if requested amount exceeded buffer limits)
            decimal effectivePrincipal = affordabilityResult.ApprovedPrincipalAmount;

            // 6. CALCULATE NCA COMPLIANT FEES & INTEREST USING CONFIGURATION VALUES
            decimal baseInitiationFee = _settings.BaseInitiationFee;
            if (effectivePrincipal > _settings.InitiationFeeThreshold)
            {
                baseInitiationFee += (effectivePrincipal - _settings.InitiationFeeThreshold) * _settings.InitiationFeePercentageAboveThreshold;
            }
            decimal initiationFee = Math.Min(baseInitiationFee, _settings.MaxInitiationFeeCap);

            decimal monthlyServiceFee = _settings.MonthlyServiceFee;
            decimal totalInterest = effectivePrincipal * monthlyInterestRate * request.TermMonths;
            decimal totalRepayment = effectivePrincipal + initiationFee + (monthlyServiceFee * request.TermMonths) + totalInterest;

            // 7. Save Affordability Record
            var assessment = new AffordabilityAssessment
            {
                CustomerID = customer.CustomerID,
                NetSalary = request.NetSalary,
                MonthlyExpenses = request.MonthlyExpenses,
                EmployerName = request.EmployerName,
                EmploymentType = request.EmploymentType,
                PaydayDate = request.PaydayDate
            };
            _context.AffordabilityAssessments.Add(assessment);

            // 8. Save Loan Record into Admin Review Queue (using effective principal)
            var loan = new Loan
            {
                CustomerID = customer.CustomerID,
                PrincipalAmount = effectivePrincipal,
                TermMonths = request.TermMonths,
                InitiationFee = initiationFee,
                MonthlyServiceFee = monthlyServiceFee,
                InterestRatePM = monthlyInterestRate,
                TotalRepayment = totalRepayment,
                BalanceRemaining = totalRepayment,
                LoanStatus = affordabilityResult.RequiresAdjustment ? "PendingCustomerConsent" : "PendingAdminReview",
                DebiCheckStatus = "Pending Mandate",
                AdminNotes = affordabilityResult.RequiresAdjustment
        ? $"Auto-adjusted from requested R{request.PrincipalAmount} to R{effectivePrincipal} due to affordability limits and safety buffer. Awaiting customer consent."
        : "Passed affordability assessment with required buffer."
            };
            _context.Loans.Add(loan);

            await _context.SaveChangesAsync();

            await LogLoanHistoryAsync(
                loan.LoanID,
                customer.IDNumber,
                "Created",
                "None",
                loan.LoanStatus,
                loan.TotalRepayment,
                loan.BalanceRemaining,
                loan.AdminNotes
            );

            return Ok(new
            {
                message = affordabilityResult.RequiresAdjustment
                    ? "Loan application adjusted for affordability and queued for compliance review."
                    : "Loan application submitted successfully and queued for compliance review.",
                loanId = loan.LoanID,
                originalRequestedAmount = request.PrincipalAmount,
                approvedPrincipalAmount = loan.PrincipalAmount,
                wasAdjusted = affordabilityResult.RequiresAdjustment,
                adjustmentMessage = affordabilityResult.Message,
                estimatedMonthlyInstallment = affordabilityResult.EstimatedMonthlyInstallment,
                remainingBuffer = affordabilityResult.RemainingBuffer,
                totalRepayment = loan.TotalRepayment,
                monthlyInterestRateApplied = monthlyInterestRate,
                status = loan.LoanStatus
            });
        }

        [HttpGet("status/{idNumber}")]
        public async Task<IActionResult> GetCustomerLoanStatus(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber))
            {
                return BadRequest(new { message = "A valid 13-digit South African ID number is required." });
            }

            if (idNumber.Length != 13)
            {
                return BadRequest(new { message = "A valid 13-digit South African ID number is required." });
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.IDNumber == idNumber);

            if (customer == null)
            {
                return NotFound(new { message = "No customer record found for this ID number." });
            }

            var loans = await _context.Loans
                .Where(l => l.CustomerID == customer.CustomerID)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new LoanSummaryDto
                {
                    LoanID = l.LoanID,
                    PrincipalAmount = l.PrincipalAmount,
                    TermMonths = l.TermMonths,
                    TotalRepayment = l.TotalRepayment,
                    MonthlyInstallment = l.TermMonths > 0 ? Math.Round(l.TotalRepayment / l.TermMonths, 2) : l.TotalRepayment,
                    BalanceRemaining = l.BalanceRemaining,
                    IsOverdue = l.BalanceRemaining > 0 && l.LoanStatus == "Defaulted",
                    LoanStatus = l.LoanStatus,
                    DebiCheckStatus = l.DebiCheckStatus,
                    MandateReference = l.MandateReference,
                    AdminNotes = l.AdminNotes,
                    CreatedAt = l.CreatedAt
                })
                .ToListAsync();

            var response = new CustomerPortalStatusDto
            {
                IDNumber = customer.IDNumber,
                FullName = $"{customer.FirstName} {customer.Surname}",
                CustomerStatus = customer.CustomerAccountStatus,
                IsBlacklisted = customer.IsBlacklisted,
                IsUnderDebtReview = customer.IsUnderDebtReview,
                Loans = loans
            };

            return Ok(response);
        }

        private bool ValidateSouthAfricanAge(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber) || idNumber.Length != 13) return false;

            string yy = idNumber.Substring(0, 2);
            string mm = idNumber.Substring(2, 2);
            string dd = idNumber.Substring(4, 2);

            int yearVal = int.Parse(yy);
            int century = (yearVal > 26) ? 1900 : 2000;
            int fullYear = century + yearVal;

            if (DateTime.TryParseExact($"{fullYear}-{mm}-{dd}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime birthDate))
            {
                var today = DateTime.Today;
                int age = today.Year - birthDate.Year;
                if (birthDate.Date > today.AddYears(-age)) age--;

                return age >= 18;
            }

            return false;
        }

        [HttpPost("{loanId}/process-skipped-payment")]
        public async Task<IActionResult> ProcessSkippedPayment(int loanId)
        {
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.LoanID == loanId);

            if (loan == null)
            {
                return NotFound(new { message = "Loan record not found." });
            }

            if (loan.BalanceRemaining <= 0)
            {
                return BadRequest(new { message = "Loan is already fully paid." });
            }

            // NCA Default Penalty Interest calculation (5% per month on overdue portion)
            decimal defaultInterestRate = 0.05m;
            decimal monthlyInstallment = loan.TotalRepayment / loan.TermMonths;
            decimal penaltyAmount = Math.Round(monthlyInstallment * defaultInterestRate, 2);

            // Enforce statutory In Duplum limit (total cost cannot exceed double original principal)
            decimal maxAllowedTotal = loan.PrincipalAmount * 2;
            if ((loan.TotalRepayment + penaltyAmount) <= maxAllowedTotal)
            {
                loan.TotalRepayment += penaltyAmount;
                loan.BalanceRemaining += penaltyAmount;
                loan.LoanStatus = "Defaulted";
                loan.AdminNotes += $" | Missed payment recorded. NCA penalty interest of R{penaltyAmount} added.";
            }
            else
            {
                loan.LoanStatus = "Defaulted";
                loan.AdminNotes += " | Missed payment recorded. Penalty capped by statutory In Duplum rule.";
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Skipped payment processed successfully.",
                loanId = loan.LoanID,
                penaltyAdded = penaltyAmount,
                newBalanceRemaining = loan.BalanceRemaining,
                newTotalRepayment = loan.TotalRepayment,
                status = loan.LoanStatus
            });
        }

        [HttpPost("consent")]
        public async Task<IActionResult> SubmitLoanConsent([FromBody] LoanConsentDto request)
        {
            if (string.IsNullOrEmpty(request.IDNumber))
            {
                return BadRequest(new { message = "ID number is required." });
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.IDNumber == request.IDNumber);

            if (customer == null)
            {
                return NotFound(new { message = "Customer not found." });
            }

            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanID == request.LoanID && l.CustomerID == customer.CustomerID);

            if (loan == null)
            {
                return NotFound(new { message = "Loan record not found for this customer." });
            }

            if (loan.LoanStatus != "PendingCustomerConsent")
            {
                return BadRequest(new { message = "This loan is not pending customer consent." });
            }

            string previousStatus = loan.LoanStatus;

            if (request.IsApprovedByCustomer)
            {
                loan.LoanStatus = "PendingAdminReview";
                loan.AdminNotes += " | Customer accepted the adjusted loan amount.";
                await _context.SaveChangesAsync();

                // Audit Log entry placed right here
                await LogLoanHistoryAsync(
                    loan.LoanID,
                    customer.IDNumber,
                    "ConsentAccepted",
                    previousStatus,
                    loan.LoanStatus,
                    loan.TotalRepayment,
                    loan.BalanceRemaining,
                    loan.AdminNotes
                );

                return Ok(new
                {
                    message = "Loan consent accepted. The application has been forwarded to the admin review queue.",
                    loanId = loan.LoanID,
                    approvedAmount = loan.PrincipalAmount,
                    monthlyInstallment = Math.Round(loan.TotalRepayment / loan.TermMonths, 2),
                    status = loan.LoanStatus
                });
            }
            else
            {
                loan.LoanStatus = "DeclinedByCustomer";
                loan.AdminNotes += " | Customer declined the adjusted loan amount.";
                await _context.SaveChangesAsync();

                // Audit Log entry placed right here
                await LogLoanHistoryAsync(
                    loan.LoanID,
                    customer.IDNumber,
                    "ConsentDeclined",
                    previousStatus,
                    loan.LoanStatus,
                    loan.TotalRepayment,
                    loan.BalanceRemaining,
                    loan.AdminNotes
                );

                return Ok(new
                {
                    message = "Loan application cancelled by customer.",
                    loanId = loan.LoanID,
                    status = loan.LoanStatus
                });
            }
        }

        /// <summary>
        /// Retrieves all loans currently waiting in the Admin Review Queue.
        /// </summary>
        [HttpGet("admin/pending")]
        public async Task<IActionResult> GetPendingAdminLoans()
        {
            var pendingLoans = await _context.Loans
                .Include(l => l.Customer)
                .Where(l => l.LoanStatus == "PendingAdminReview")
                .OrderBy(l => l.CreatedAt)
                .Select(l => new
                {
                    loanId = l.LoanID,
                    customerId = l.CustomerID,
                    customerName = l.Customer != null ? $"{l.Customer.FirstName} {l.Customer.Surname}" : "Unknown",
                    idNumber = l.Customer != null ? l.Customer.IDNumber : string.Empty,
                    principalAmount = l.PrincipalAmount,
                    termMonths = l.TermMonths,
                    totalRepayment = l.TotalRepayment,
                    adminNotes = l.AdminNotes,
                    createdAt = l.CreatedAt
                })
                .ToListAsync();

            return Ok(pendingLoans);
        }

        /// <summary>
        /// Processes an admin approval or rejection decision for a pending loan application.
        /// </summary>
        [HttpPost("admin/review")]
        public async Task<IActionResult> ReviewLoan([FromBody] AdminReviewDto request)
        {
            var loan = await _context.Loans
                .Include(l => l.Customer)
                .FirstOrDefaultAsync(l => l.LoanID == request.LoanID);

            if (loan == null)
            {
                return NotFound(new { message = "Loan record not found." });
            }

            if (loan.LoanStatus != "PendingAdminReview")
            {
                return BadRequest(new { message = $"Loan {loan.LoanID} is not pending admin review. Current status: {loan.LoanStatus}" });
            }

            string previousStatus = loan.LoanStatus;

            if (request.IsApproved)
            {
                loan.LoanStatus = "Active";
                loan.DebiCheckStatus = "Mandate Requested";
                loan.AdminNotes += $" | Admin Approved: {request.AdminNotes}";
                await _context.SaveChangesAsync();

                await LogLoanHistoryAsync(
                    loan.LoanID,
                    loan.Customer?.IDNumber ?? string.Empty,
                    "AdminApproved",
                    previousStatus,
                    loan.LoanStatus,
                    loan.TotalRepayment,
                    loan.BalanceRemaining,
                    loan.AdminNotes
                );

                return Ok(new
                {
                    message = "Loan successfully approved and activated.",
                    loanId = loan.LoanID,
                    status = loan.LoanStatus,
                    debiCheckStatus = loan.DebiCheckStatus
                });
            }
            else
            {
                loan.LoanStatus = "DeclinedByAdmin";
                loan.AdminNotes += $" | Admin Rejected: {request.AdminNotes}";
                await _context.SaveChangesAsync();

                await LogLoanHistoryAsync(
                    loan.LoanID,
                    loan.Customer?.IDNumber ?? string.Empty,
                    "AdminRejected",
                    previousStatus,
                    loan.LoanStatus,
                    loan.TotalRepayment,
                    loan.BalanceRemaining,
                    loan.AdminNotes
                );

                return Ok(new
                {
                    message = "Loan application rejected by administrator.",
                    loanId = loan.LoanID,
                    status = loan.LoanStatus
                });
            }
        }

        public class LoanApplicationDto
        {
            [Required(ErrorMessage = "South African ID number is required.")]
            [StringLength(13, MinimumLength = 13, ErrorMessage = "ID number must be exactly 13 digits.")]
            [RegularExpression(@"^\d{13}$", ErrorMessage = "ID number must contain only numbers.")]
            public string IDNumber { get; set; } = string.Empty;

            [Required]
            [Range(500.00, 3000.00, ErrorMessage = "Principal amount must be between R500 and R3,000.")]
            public decimal PrincipalAmount { get; set; }

            [Required]
            [Range(1, 3, ErrorMessage = "Term must be between 1 and 3 months.")]
            public int TermMonths { get; set; }

            [Required]
            [Range(1000.00, 1000000.00, ErrorMessage = "Net salary must be a valid positive amount.")]
            public decimal NetSalary { get; set; }

            [Required]
            [Range(0.00, 1000000.00, ErrorMessage = "Monthly expenses cannot be negative.")]
            public decimal MonthlyExpenses { get; set; }

            [Required(ErrorMessage = "Employer name is required.")]
            [StringLength(100)]
            public string EmployerName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Employment type is required.")]
            public string EmploymentType { get; set; } = "Permanent";

            [Required(ErrorMessage = "Payday date is required.")]
            public string PaydayDate { get; set; } = string.Empty;
        }

        /// <summary>
        /// Retrieves the complete audit and status history for a specific loan ID.
        /// </summary>
        [HttpGet("{loanId}/history")]
        public async Task<IActionResult> GetLoanHistory(int loanId)
        {
            var history = await _context.LoanHistories
                .Where(h => h.LoanID == loanId)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync();

            if (history.Count == 0)
            {
                return NotFound(new { message = "No audit history records found for this loan ID." });
            }

            return Ok(history);
        }


        private async Task LogLoanHistoryAsync(
            int loanId,
            string idNumber,
            string eventType,
            string prevStatus,
            string newStatus,
            decimal totalRepayment,
            decimal balanceRemaining,
            string details)
        {
            var historyRecord = new LoanHistory
            {
                LoanID = loanId,
                IDNumber = idNumber,
                EventType = eventType,
                PreviousStatus = prevStatus,
                NewStatus = newStatus,
                TotalRepayment = totalRepayment,
                BalanceRemaining = balanceRemaining,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _context.LoanHistories.Add(historyRecord);
            await _context.SaveChangesAsync();
        }

    }

}