using System.Security.AccessControl;
using System.Xml.Linq;
using MxMessageProcessor;
using MxMessageProcessor.Models;
using Oracle.ManagedDataAccess.Client;

if (args.Length == 0)
{
    Console.WriteLine("Error: XML file path was not provided.");
    FileLogger.Log("ERROR: XML file path was not provided.");
    Environment.ExitCode = 1;
    return;
}

string filePath = args[0];
bool skipDatabase = args.Skip(1).Any(arg =>
    arg.Equals("--no-db", StringComparison.OrdinalIgnoreCase));
FileLogger.Log($"Processing started. Source file: {Path.GetFileName(filePath)}");

if (!File.Exists(filePath))
{
    Console.WriteLine($"Error: File not found: {filePath}");
    FileLogger.Log($"ERROR: File not found: {filePath}");
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
    FileLogger.Log($"ERROR: Invalid XML format: {ex.Message}");
    Environment.ExitCode = 1;
    return;
}
catch (IOException ex)
{
    Console.WriteLine($"Error reading file: {ex.Message}");
    FileLogger.Log($"ERROR: Failed to read XML file: {ex.Message}");
    Environment.ExitCode = 1;
    return;
}
catch (UnauthorizedAccessException)
{
    Console.WriteLine("Error: Access to the XML file was denied.");
    FileLogger.Log("ERROR: Access to XML file was denied.");
    Environment.ExitCode = 1;
    return;
}

XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08";

string? messageId = xml
    .Descendants(ns + "MsgId")
    .FirstOrDefault()?.Value;

if (string.IsNullOrWhiteSpace(messageId))
{
    Console.WriteLine("ERROR: Message ID is missing or empty.");
    FileLogger.Log("ERROR: Message ID is missing or empty.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Message ID: {messageId}");

var transactions = xml
    .Descendants(ns + "CdtTrfTxInf")
    .ToList();

Console.WriteLine($"Transactions found: {transactions.Count}");
FileLogger.Log($"Transactions found: {transactions.Count}");
string? expectedCountText = xml
    .Descendants(ns + "GrpHdr")
    .Elements(ns + "NbOfTxs")
    .FirstOrDefault()?
    .Value;

if (!int.TryParse(expectedCountText, out int expectedCount))
{
    Console.WriteLine("Error: Invalid NbOfTxs value.");
    FileLogger.Log("ERROR: Invalid or missing NbOfTxs value.");
    Environment.ExitCode = 1;
    return;
}

int actualCount = transactions.Count;

if (expectedCount != actualCount)
{
    Console.WriteLine(
        $"Validation failed! Expected: {expectedCount}, Actual: {actualCount}"
    );
    FileLogger.Log($"ERROR: Transaction count mismatch. Expected: {expectedCount}, Actual: {actualCount}");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("Validation successful!");
List<MxTransaction> parsedTransactions = new();
string rawXml = xml.ToString();
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
        FileLogger.Log($"ERROR: Missing required field in transaction {endToEndId}");
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
        FileLogger.Log($"ERROR: Invalid amount in transaction {endToEndId}");
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
        FileLogger.Log($"ERROR: Invalid date in transaction {endToEndId}");
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
        RawXml = rawXml,
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
if (skipDatabase)
{
    Console.WriteLine(
        "XML processing completed successfully. Database access skipped."
    );

    FileLogger.Log(
        $"SUCCESS: XML validation completed without database access. " +
        $"Message ID: {messageId}, Transactions: {parsedTransactions.Count}"
    );

    return;
}

string? dbPassword =
    Environment.GetEnvironmentVariable("MX_DB_PASSWORD");

if (string.IsNullOrWhiteSpace(dbPassword))
{
    Console.WriteLine("Error: MX_DB_PASSWORD is not configured.");

    FileLogger.Log("ERROR: MX_DB_PASSWORD is not configured.");

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
    using OracleCommand duplicateCheck = connection.CreateCommand();

    duplicateCheck.CommandText = @"
    SELECT COUNT(*)
    FROM MX_TRANSACTIONS
    WHERE MSG_ID = :msgId";

    duplicateCheck.Parameters.Add("msgId", OracleDbType.Varchar2).Value = messageId;

    int existingCount = Convert.ToInt32(duplicateCheck.ExecuteScalar());

    if (existingCount > 0)
    {
        Console.WriteLine(
            $"Duplicate message detected: {messageId}. Skipping message."
        );
        FileLogger.Log($"Duplicate message detected: {messageId}. Skipping message.");
        return;
    }
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
        FileLogger.Log($"SUCCESS: All {parsedTransactions.Count} transactions saved successfully. Message ID: {messageId}");
    }
    catch (Exception ex)
    {
        dbTransaction.Rollback();

        Console.WriteLine("Transaction rolled back. No changes were saved.");

        FileLogger.Log(
            $"ERROR: Transaction rolled back. Message ID: {messageId}. Reason: {ex.Message}");

        throw;
    }

}
catch (OracleException ex) when (ex.Number == 1)
{
    Console.WriteLine(
        "Duplicate transaction detected. A unique constraint was violated."
    );

    FileLogger.Log(
        "ERROR: Duplicate transaction detected. A unique constraint was violated."
    );

    Environment.ExitCode = 1;
}
catch (OracleException ex)
{
    Console.WriteLine($"Oracle database error: {ex.Message}");
    FileLogger.Log($"ERROR: Oracle database error: {ex.Message}");
    Environment.ExitCode = 1;
}

catch (Exception ex)
{
    Console.WriteLine($"Unexpected error: {ex.Message}");
    FileLogger.Log($"ERROR: Unexpected error: {ex.Message}");
    Environment.ExitCode = 1;
}
