using System.Xml.Linq;
using MxMessageProcessor.Models;

if (args.Length == 0)
{
    Console.WriteLine("Error: XML file path was not provided.");
    return;
}

string filePath = args[0];

if (!File.Exists(filePath))
{
    Console.WriteLine($"Error: File not found: {filePath}");
    return;
}

XDocument xml;

try
{
    xml = XDocument.Load(filePath);
}
catch (System.Xml.XmlException ex)
{
    Console.WriteLine($"Error: Invalid XML format. {ex.Message}");
    return;
}
catch (IOException ex)
{
    Console.WriteLine($"Error reading file: {ex.Message}");
    return;
}
catch (UnauthorizedAccessException)
{
    Console.WriteLine("Error: Access to the XML file was denied.");
    return;
}

XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08";

string? messageId = xml
    .Descendants(ns + "MsgId")
    .FirstOrDefault()?.Value;

Console.WriteLine($"Message ID: {messageId}");

var transactions = xml
    .Descendants(ns + "CdtTrfTxInf")
    .ToList();

Console.WriteLine($"Transactions found: {transactions.Count}");
string? expectedCountText = xml
    .Descendants(ns + "GrpHdr")
    .Elements(ns + "NbOfTxs")
    .FirstOrDefault()?
    .Value;

if (!int.TryParse(expectedCountText, out int expectedCount))
{
    Console.WriteLine("Error: Invalid NbOfTxs value.");
    return;
}

int actualCount = transactions.Count;

if (expectedCount != actualCount)
{
    Console.WriteLine(
        $"Validation failed! Expected: {expectedCount}, Actual: {actualCount}"
    );
    return;
}

Console.WriteLine("Validation successful!");
List<MxTransaction> parsedTransactions = new();

foreach (var transaction in transactions)
{
    string? endToEndId = transaction
        .Element(ns + "PmtId")?
        .Element(ns + "EndToEndId")?
        .Value;

    string? amountText = transaction
        .Element(ns + "IntrBkSttlmAmt")?
        .Value;

    string? currency = transaction
        .Element(ns + "IntrBkSttlmAmt")?
        .Attribute("Ccy")?
        .Value;

    string? settlementDateText = transaction
    .Element(ns + "IntrBkSttlmDt")?
    .Value;

    string? debtorName = transaction
        .Element(ns + "Dbtr")?
        .Element(ns + "Nm")?
        .Value;

    string? debtorBic = transaction
        .Element(ns + "DbtrAgt")?
        .Element(ns + "FinInstnId")?
        .Element(ns + "BICFI")?
        .Value;

    string? creditorName = transaction
        .Element(ns + "Cdtr")?
        .Element(ns + "Nm")?
        .Value;

    string? creditorBic = transaction
        .Element(ns + "CdtrAgt")?
        .Element(ns + "FinInstnId")?
        .Element(ns + "BICFI")?
        .Value;

    if (string.IsNullOrWhiteSpace(endToEndId) ||
    string.IsNullOrWhiteSpace(amountText) ||
    string.IsNullOrWhiteSpace(currency) ||
    string.IsNullOrWhiteSpace(settlementDateText))
    {
        Console.WriteLine(
            $"Validation failed: Missing required field in transaction {endToEndId}"
        );
        return;
    }

    if (!decimal.TryParse(
    amountText,
    System.Globalization.NumberStyles.Number,
    System.Globalization.CultureInfo.InvariantCulture,
    out decimal parsedAmount))
    {
        Console.WriteLine(
            $"Validation failed: Invalid amount in transaction {endToEndId}"
        );
        return;
    }

    if (!DateTime.TryParseExact(
        settlementDateText,
        "yyyy-MM-dd",
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None,
        out DateTime parsedDate))
    {
        Console.WriteLine(
            $"Validation failed: Invalid date in transaction {endToEndId}"
        );
        return;
    }

    MxTransaction mxTransaction = new MxTransaction
    {
        MessageId = messageId,
        MessageType = "pacs.008",
        EndToEndId = endToEndId,

        Amount = parsedAmount,
        Currency = currency,
        SettlementDate = parsedDate,

        DebtorName = debtorName,
        DebtorBic = debtorBic,

        CreditorName = creditorName,
        CreditorBic = creditorBic,

        RawXml = xml.ToString(),
        LoadTimestamp = DateTime.UtcNow,
        SourceFile = Path.GetFileName(filePath),
        Status = "PARSED"
    };

    parsedTransactions.Add(mxTransaction);

    Console.WriteLine($"Debtor: {mxTransaction.DebtorName}");
    Console.WriteLine($"Debtor BIC: {mxTransaction.DebtorBic}");
    Console.WriteLine($"Creditor: {mxTransaction.CreditorName}");
    Console.WriteLine($"Creditor BIC: {mxTransaction.CreditorBic}");
    Console.WriteLine($"Settlement Date: {mxTransaction.SettlementDate:yyyy-MM-dd}");
    Console.WriteLine("------------------------------");

    Console.WriteLine(
        $"Transaction: {mxTransaction.EndToEndId}, " +
        $"Amount: {mxTransaction.Amount}, " +
        $"Currency: {mxTransaction.Currency}"
    );
}