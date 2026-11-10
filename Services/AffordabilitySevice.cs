using System;

namespace QuickVault.Api.Services
{
    public interface IAffordabilityService
    {
        AffordabilityResult EvaluateAffordability(
            decimal netSalary, 
            decimal monthlyExpenses, 
            decimal requestedPrincipal, 
            int termMonths, 
            decimal interestRatePM);
    }

    public class AffordabilityResult
    {
        public bool IsAffordable { get; set; }
        public bool RequiresAdjustment { get; set; }
        public decimal ApprovedPrincipalAmount { get; set; }
        public decimal EstimatedMonthlyInstallment { get; set; }
        public decimal DisposableIncome { get; set; }
        public decimal RemainingBuffer { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class AffordabilityService : IAffordabilityService
    {
        // Mandatory safety buffer remaining after loan installment
        private const decimal MandatoryBuffer = 500.00m; 

        public AffordabilityResult EvaluateAffordability(
            decimal netSalary, 
            decimal monthlyExpenses, 
            decimal requestedPrincipal, 
            int termMonths, 
            decimal interestRatePM)
        {
            decimal disposableIncome = netSalary - monthlyExpenses;
            decimal maxAvailableForLoan = disposableIncome - MandatoryBuffer;

            if (maxAvailableForLoan <= 0)
            {
                return new AffordabilityResult
                {
                    IsAffordable = false,
                    RequiresAdjustment = false,
                    ApprovedPrincipalAmount = 0,
                    EstimatedMonthlyInstallment = 0,
                    DisposableIncome = disposableIncome,
                    RemainingBuffer = disposableIncome,
                    Message = "Declined: Net income after expenses does not meet the mandatory buffer requirement."
                };
            }

            decimal requestedInstallment = CalculateMonthlyInstallment(requestedPrincipal, termMonths, interestRatePM);

            if (requestedInstallment <= maxAvailableForLoan)
            {
                return new AffordabilityResult
                {
                    IsAffordable = true,
                    RequiresAdjustment = false,
                    ApprovedPrincipalAmount = requestedPrincipal,
                    EstimatedMonthlyInstallment = requestedInstallment,
                    DisposableIncome = disposableIncome,
                    RemainingBuffer = disposableIncome - requestedInstallment,
                    Message = "Affordable: Requested loan fits within income parameters with the required buffer."
                };
            }

            decimal adjustedPrincipal = CalculateMaxPrincipalFromInstallment(maxAvailableForLoan, termMonths, interestRatePM);

            if (adjustedPrincipal < 500.00m)
            {
                return new AffordabilityResult
                {
                    IsAffordable = false,
                    RequiresAdjustment = false,
                    ApprovedPrincipalAmount = 0,
                    EstimatedMonthlyInstallment = 0,
                    DisposableIncome = disposableIncome,
                    RemainingBuffer = disposableIncome,
                    Message = "Declined: Maximum affordable loan amount falls below the minimum lending threshold."
                };
            }

            decimal adjustedInstallment = CalculateMonthlyInstallment(adjustedPrincipal, termMonths, interestRatePM);

            return new AffordabilityResult
            {
                IsAffordable = true,
                    RequiresAdjustment = true,
                    ApprovedPrincipalAmount = Math.Round(adjustedPrincipal, 2),
                    EstimatedMonthlyInstallment = Math.Round(adjustedInstallment, 2),
                    DisposableIncome = disposableIncome,
                    RemainingBuffer = disposableIncome - adjustedInstallment,
                    Message = "Adjusted: Requested amount exceeds affordability. Automatically qualified for a reduced principal amount."
            };
        }

        private decimal CalculateMonthlyInstallment(decimal principal, int months, decimal monthlyRate)
        {
            if (monthlyRate == 0) return principal / months;
            double r = (double)monthlyRate;
            double n = months;
            double p = (double)principal;
            double pmt = p * r * Math.Pow(1 + r, n) / (Math.Pow(1 + r, n) - 1);
            return (decimal)pmt;
        }

        private decimal CalculateMaxPrincipalFromInstallment(decimal maxInstallment, int months, decimal monthlyRate)
        {
            if (monthlyRate == 0) return maxInstallment * months;
            double r = (double)monthlyRate;
            double n = months;
            double pmt = (double)maxInstallment;
            double p = pmt * (Math.Pow(1 + r, n) - 1) / (r * Math.Pow(1 + r, n));
            return (decimal)p;
        }
    }
}