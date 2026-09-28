using OrderProcessing.Application;

namespace OrderProcessing.Infrastructure;

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("Email to {To}: {Subject} - {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
