using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QuickVault.Api.Models
{
    public class CustomerPortalStatusDto
    {
        public string IDNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CustomerStatus { get; set; } = string.Empty;
        public bool IsBlacklisted { get; set; }
        public bool IsUnderDebtReview { get; set; }
        public List<LoanSummaryDto> Loans { get; set; } = new();
    }

    public class LoanSummaryDto
    {
        public int LoanID { get; set; }
        public decimal PrincipalAmount { get; set; }
        public int TermMonths { get; set; }
        public decimal TotalRepayment { get; set; }
        public decimal MonthlyInstallment { get; set; } // Displays the monthly repayment explicitly
        public decimal BalanceRemaining { get; set; }
        public bool IsOverdue { get; set; }
        public int DaysOverdue { get; set; }
        public decimal PenaltyInterestApplied { get; set; }
        public string LoanStatus { get; set; } = string.Empty;
        public string DebiCheckStatus { get; set; } = string.Empty;
        public string MandateReference { get; set; } = string.Empty;
        public string? AdminNotes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    public class Customer
    {
        [Key]
        public int CustomerID { get; set; }
        [Required]
        [MaxLength(13)]
        public string IDNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string CommunicationPreference { get; set; } = string.Empty;

        // New Profile Lifecycles: Active, PendingFICA, Suspended, Blacklisted, UnderDebtReview
        public string CustomerAccountStatus { get; set; } = "Active";
        public bool IsBlacklisted { get; set; } = false;
        public bool IsUnderDebtReview { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class OTPSession
    {
        [Key]
        public int OTPID { get; set; }
        [Required]
        [MaxLength(13)]
        public string IDNumber { get; set; } = string.Empty;
        [Required]
        [MaxLength(6)]
        public string OTPCode { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; } = false;
    }

    public class AffordabilityAssessment
    {
        [Key]
        public int AssessmentID { get; set; }
        public int CustomerID { get; set; }
        public decimal NetSalary { get; set; }
        public decimal MonthlyExpenses { get; set; }
        public string EmployerName { get; set; } = string.Empty;
        public string EmploymentType { get; set; } = "Permanent";
        public string PaydayDate { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class Loan
    {
        [Key]
        public int LoanID { get; set; }
        public int CustomerID { get; set; }
        public Customer? Customer { get; set; }
        public decimal PrincipalAmount { get; set; }
        public int TermMonths { get; set; }
        public decimal InitiationFee { get; set; }
        public decimal MonthlyServiceFee { get; set; }
        public decimal InterestRatePM { get; set; } = 0.0250m; // 2.5% monthly (30% p.a.)
        public decimal TotalRepayment { get; set; }
        public decimal BalanceRemaining { get; set; }

        // Comprehensive Loan Lifecycles: 
        // PendingAssessment, PendingAdminReview, PendingDebiCheck, Approved, Rejected, Active, Settled, Defaulted
        public string LoanStatus { get; set; } = "PendingAssessment";
        public string DebiCheckStatus { get; set; } = "Pending Auth";
        public string? MandateReference { get; set; } // Tracks the gateway tracking ref
        public string? AdminNotes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class QuickVaultDbContext : DbContext
    {
        public QuickVaultDbContext(DbContextOptions<QuickVaultDbContext> options) : base(options) { }

        public DbSet<Customer> Customers { get; set; }
        public DbSet<OTPSession> OTPSessions { get; set; }
        public DbSet<AffordabilityAssessment> AffordabilityAssessments { get; set; }
        public DbSet<Loan> Loans { get; set; }
        public DbSet<LoanHistory> LoanHistories { get; set; }
    }

    public class LoanConsentDto
    {
        public int LoanID { get; set; }
        public string IDNumber { get; set; } = string.Empty;
        public bool IsApprovedByCustomer { get; set; }
    }

    public class LoanHistory
    {
        public int LoanHistoryID { get; set; }
        public int LoanID { get; set; }
        public string IDNumber { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty; // e.g., "Created", "ConsentAccepted", "AdminApproved", "PaymentSkipped", "Defaulted"
        public string PreviousStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public decimal TotalRepayment { get; set; }
        public decimal BalanceRemaining { get; set; }
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class AdminReviewDto
    {
        public int LoanID { get; set; }
        public bool IsApproved { get; set; }
        public string AdminNotes { get; set; } = string.Empty;
    }
}