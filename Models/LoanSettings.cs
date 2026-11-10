namespace QuickVault.Api.Models
{
    public class LoanSettings
    {
        public decimal BaseInitiationFee { get; set; }
        public decimal InitiationFeeThreshold { get; set; }
        public decimal InitiationFeePercentageAboveThreshold { get; set; }
        public decimal MaxInitiationFeeCap { get; set; }
        public decimal MonthlyServiceFee { get; set; }
        public decimal MonthlyInterestRate { get; set; }
    }
}