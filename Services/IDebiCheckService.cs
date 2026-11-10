public interface IDebiCheckService
{
    Task<DebiCheckStatusResult> CheckMandateStatusAsync(string mandateRef);
}

public class DebiCheckStatusResult
{
    public string MandateRef { get; set; } = string.Empty;
    public bool IsAccepted { get; set; }
    public bool IsDeclined { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

public class DebiCheckService : IDebiCheckService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DebiCheckService> _logger;

    public DebiCheckService(HttpClient httpClient, ILogger<DebiCheckService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DebiCheckStatusResult> CheckMandateStatusAsync(string mandateRef)
    {
        // TODO: Replace this with the actual API endpoint of your DebiCheck / banking provider
        _logger.LogInformation("Polling DebiCheck gateway for MandateRef: {MandateRef}", mandateRef);

        await Task.Delay(100); // Simulate network latency

        // Simulated response (In production, parse JSON response from bank API)
        return new DebiCheckStatusResult
        {
            MandateRef = mandateRef,
            IsAccepted = true,  // Change based on bank response
            IsDeclined = false,
            StatusMessage = "Mandate authenticated and accepted by customer."
        };
    }
}