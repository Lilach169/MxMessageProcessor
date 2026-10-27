using System.Xml.Linq;
using MxMessageProcessor.Models;
using Oracle.ManagedDataAccess.Client;

if (args.Length == 0)
{
    Console.WriteLine("Error: XML file path was not provided.");
    Environment.ExitCode = 1;
    return;
}

string filePath = args[0];

if (!File.Exists(filePath))
{
    Console.WriteLine($"Error: File not found: {filePath}");
    Environment.ExitCode = 1;
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
    Environment.ExitCode = 1;
    return;
}
catch (IOException ex)
{
    Console.WriteLine($"Error reading file: {ex.Message}");
    Environment.ExitCode = 1;
    return;
}
catch (UnauthorizedAccessException)
{
    Console.WriteLine("Error: Access to the XML file was denied.");
    Environment.ExitCode = 1;
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
    Environment.ExitCode = 1;
    return;
}

int actualCount = transactions.Count;

if (expectedCount != actualCount)
{
    Console.WriteLine(
        $"Validation failed! Expected: {expectedCount}, Actual: {actualCount}"
    );
    Environment.ExitCode = 1;
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
        Environment.ExitCode = 1;
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
        Environment.ExitCode = 1;
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
        Environment.ExitCode = 1;
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

string? dbPassword =
    Environment.GetEnvironmentVariable("MX_DB_PASSWORD");

if (string.IsNullOrWhiteSpace(dbPassword))
{
    Console.WriteLine("Error: MX_DB_PASSWORD is not configured.");
    Environment.ExitCode = 1;
    return;
}

string connectionString =
    $"User Id=MX_APP;Password={dbPassword};Data Source=//localhost:1521/FREEPDB1;";
try
{
    using OracleConnection connection = new OracleConnection(connectionString);
    connection.Open();
    Console.WriteLine("Successfully connected to Oracle!");
    Console.WriteLine($"Ready to save {parsedTransactions.Count} transactions to Oracle.");
    using OracleTransaction dbTransaction = connection.BeginTransaction();
    try
    {
        foreach (MxTransaction transaction in parsedTransactions)
    {
        using OracleCommand command = connection.CreateCommand();
        command.Transaction = dbTransaction;

        command.BindByName = true;

        command.CommandText = @"
        INSERT INTO MX_TRANSACTIONS
        (MSG_ID, MSG_TYPE, END_TO_END_ID, AMOUNT, CURRENCY,
         SETTLEMENT_DATE, DEBTOR_NAME, DEBTOR_BIC,
         CREDITOR_NAME, CREDITOR_BIC, RAW_XML,
         LOAD_TIMESTAMP, SOURCE_FILE, STATUS)
        VALUES
        (:msgId, :msgType, :endToEndId, :amount, :currency,
         :settlementDate, :debtorName, :debtorBic,
         :creditorName, :creditorBic, :rawXml,
         :loadTimestamp, :sourceFile, :status)";

        command.Parameters.Add("msgId", OracleDbType.Varchar2).Value =
    (object?)transaction.MessageId ?? DBNull.Value;

        command.Parameters.Add("msgType", OracleDbType.Varchar2).Value =
            (object?)transaction.MessageType ?? DBNull.Value;

        command.Parameters.Add("endToEndId", OracleDbType.Varchar2).Value =
            transaction.EndToEndId;

        command.Parameters.Add("amount", OracleDbType.Decimal).Value =
            transaction.Amount;

        command.Parameters.Add("currency", OracleDbType.Varchar2).Value =
            transaction.Currency;

        command.Parameters.Add("settlementDate", OracleDbType.Date).Value =
            transaction.SettlementDate;

        command.Parameters.Add("debtorName", OracleDbType.NVarchar2).Value =
            (object?)transaction.DebtorName ?? DBNull.Value;

        command.Parameters.Add("debtorBic", OracleDbType.Varchar2).Value =
            (object?)transaction.DebtorBic ?? DBNull.Value;

        command.Parameters.Add("creditorName", OracleDbType.NVarchar2).Value =
            (object?)transaction.CreditorName ?? DBNull.Value;

        command.Parameters.Add("creditorBic", OracleDbType.Varchar2).Value =
            (object?)transaction.CreditorBic ?? DBNull.Value;

        command.Parameters.Add("rawXml", OracleDbType.Clob).Value =
            (object?)transaction.RawXml ?? DBNull.Value;

        command.Parameters.Add("loadTimestamp", OracleDbType.TimeStamp).Value =
            transaction.LoadTimestamp;

        command.Parameters.Add("sourceFile", OracleDbType.Varchar2).Value =
            (object?)transaction.SourceFile ?? DBNull.Value;

        command.Parameters.Add("status", OracleDbType.Varchar2).Value =
            transaction.Status;

        int rowsInserted = command.ExecuteNonQuery();

        Console.WriteLine(
            $"Transaction {transaction.EndToEndId}: {rowsInserted} row inserted."
        );
    }
    dbTransaction.Commit();

    Console.WriteLine("All transactions saved successfully!");
    }
    catch (Exception)
    {
        dbTransaction.Rollback();
        Console.WriteLine("Transaction rolled back. No changes were saved.");
        throw;
    }

}
catch (OracleException ex) when (ex.Number == 1)
{
    Console.WriteLine(
        "Duplicate transaction detected. The transaction already exists in the database."
    );
    Environment.ExitCode = 1;
}
catch (OracleException ex)
{
    Console.WriteLine($"Oracle database error: {ex.Message}");
    Environment.ExitCode = 1;
}