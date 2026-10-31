# MxMessageProcessor

## Description

A .NET 8 console application for processing ISO 20022 pacs.008 XML messages.

The application reads an XML file, validates the data, extracts payment transactions and saves them to an Oracle database.

## Technologies

- C# / .NET 8
- LINQ to XML
- Oracle Database
- Oracle.ManagedDataAccess.Core

## Requirements

- .NET 8 SDK
- Oracle Database for database operations
- MX_APP database user
- MX_TRANSACTIONS table

## Database Setup

The SQL script for creating the database table is located at:

`MxMessageProcessor/create_tables.sql`

The application uses the following Oracle connection:

`localhost:1521/FREEPDB1`

The database password is read from the `MX_DB_PASSWORD` environment variable.

Set the password in PowerShell:

```powershell
$env:MX_DB_PASSWORD = "your_password"
```

## Build

Run from the repository root:

```powershell
dotnet build ".\MxMessageProcessor\MxMessageProcessor.csproj"
```

## Run

Process an XML file and save transactions to Oracle:

```powershell
dotnet run --project ".\MxMessageProcessor\MxMessageProcessor.csproj" -- ".\MxMessageProcessor\Samples\sample-pacs008.xml"
```

Run without a database connection:

```powershell
dotnet run --project ".\MxMessageProcessor\MxMessageProcessor.csproj" -- ".\MxMessageProcessor\Samples\sample-pacs008.xml" --no-db
```

## Processing

1. Read the XML file.
2. Validate the message ID and transaction count.
3. Extract transaction details.
4. Validate required fields, amounts and dates.
5. Check for an existing message in Oracle.
6. Insert transactions using parameterized SQL.
7. Commit the transaction or roll back if an insert fails.
8. Log processing results and errors.

## Test Files

Sample XML files are available in the `MxMessageProcessor/Samples` directory.

The samples are used to test XML validation, successful processing and database rollback.

## Notes

- Duplicate messages are checked using MsgId.
- Database inserts are performed within a single transaction.
- The `--no-db` option allows XML processing without Oracle.
- Database credentials are not stored in the source code.
