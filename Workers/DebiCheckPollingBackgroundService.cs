using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using QuickVault.Api.Models;

public class DebiCheckPollingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DebiCheckPollingBackgroundService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(5); // Runs every 5 minutes

    public DebiCheckPollingBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DebiCheckPollingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DebiCheck Polling Background Service has started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollPendingMandatesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while polling DebiCheck statuses.");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("DebiCheck Polling Background Service is stopping.");
    }

    private async Task PollPendingMandatesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuickVaultDbContext>();
        var debiCheckService = scope.ServiceProvider.GetRequiredService<IDebiCheckService>();

        // Query loans waiting for mandate verification from DebiCheck
        var pendingLoans = await dbContext.Loans
            .Include(l => l.Customer)
            .Where(l => l.DebiCheckStatus == "Mandate Requested" || l.LoanStatus == "PendingDebiCheck")
            .ToListAsync(stoppingToken);

        if (!pendingLoans.Any()) return;

        _logger.LogInformation("Found {Count} loans pending DebiCheck verification.", pendingLoans.Count);

        foreach (var loan in pendingLoans)
        {
            if (stoppingToken.IsCancellationRequested) break;

            string trackingRef = loan.MandateReference ?? loan.LoanID.ToString();
            var result = await debiCheckService.CheckMandateStatusAsync(trackingRef);

            string previousStatus = loan.LoanStatus;

            if (result.IsAccepted)
            {
                loan.LoanStatus = "Active";
                loan.DebiCheckStatus = "Active";
                loan.AdminNotes += " | DebiCheck mandate authenticated successfully.";

                _logger.LogInformation("Loan ID {LoanId} mandate accepted. Status updated to Active.", loan.LoanID);

                // Audit Trail Log
                dbContext.LoanHistories.Add(new LoanHistory
                {
                    LoanID = loan.LoanID,
                    IDNumber = loan.Customer?.IDNumber ?? string.Empty,
                    EventType = "DebiCheckAccepted",
                    PreviousStatus = previousStatus,
                    NewStatus = loan.LoanStatus,
                    TotalRepayment = loan.TotalRepayment,
                    BalanceRemaining = loan.BalanceRemaining,
                    Details = "DebiCheck mandate authenticated and active.",
                    Timestamp = DateTime.UtcNow
                });
            }
            else if (result.IsDeclined)
            {
                loan.LoanStatus = "FailedDebiCheck";
                loan.DebiCheckStatus = "Failed";
                loan.AdminNotes += " | DebiCheck mandate authentication failed or was rejected by customer.";

                _logger.LogWarning("Loan ID {LoanId} mandate declined. Status updated to FailedDebiCheck.", loan.LoanID);

                // Audit Trail Log
                dbContext.LoanHistories.Add(new LoanHistory
                {
                    LoanID = loan.LoanID,
                    IDNumber = loan.Customer?.IDNumber ?? string.Empty,
                    EventType = "DebiCheckDeclined",
                    PreviousStatus = previousStatus,
                    NewStatus = loan.LoanStatus,
                    TotalRepayment = loan.TotalRepayment,
                    BalanceRemaining = loan.BalanceRemaining,
                    Details = "DebiCheck mandate failed or declined.",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }
}