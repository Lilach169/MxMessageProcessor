namespace MxMessageProcessor.Models;

public class MxTransaction
{
    public string? MessageId { get; set; }
    public string? MessageType { get; set; }
    public string? EndToEndId { get; set; }

    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime SettlementDate { get; set; }

    public string? DebtorName { get; set; }
    public string? DebtorBic { get; set; }

    public string? CreditorName { get; set; }
    public string? CreditorBic { get; set; }

    public string? RawXml { get; set; }
    public DateTime LoadTimestamp { get; set; }
    public string? SourceFile { get; set; }
    public string? Status { get; set; }
}