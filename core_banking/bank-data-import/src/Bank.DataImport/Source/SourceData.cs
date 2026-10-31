using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Bank.DataImport.Source;

public sealed record SourceCustomer(string Id, string FirstName, string LastName, DateOnly DateOfBirth, string Email, string Phone, string Address);

public sealed record SourceAccount(string Id, string CustomerId, string Type);

/// <summary>Kind: deposit, withdrawal or transfer (a transfer needs TargetAccountId).</summary>
public sealed record SourceTransaction(string Id, string AccountId, DateOnly Date, string Kind, decimal Amount, string? TargetAccountId);

public sealed record SourceLoan(string Id, string CustomerId, string AccountId, string Type, decimal Amount, int TermMonths, DateOnly StartDate);

public sealed record SourceLoanPayment(string Id, string LoanId, DateOnly Date, decimal Amount, bool Late);

public sealed record SourceStandingOrder(string Id, string AccountId, string TargetAccountId, decimal Amount, string Frequency, DateOnly StartDate, DateOnly? EndDate);

public sealed record SourceData(
    IReadOnlyList<SourceCustomer> Customers, IReadOnlyList<SourceAccount> Accounts, IReadOnlyList<SourceTransaction> Transactions,
    IReadOnlyList<SourceLoan> Loans, IReadOnlyList<SourceLoanPayment> LoanPayments, IReadOnlyList<SourceStandingOrder> StandingOrders);

/// <summary>
/// Reads the CSV export (stage 7 file, step 1). The dataset's column names are mapped through configuration
/// (Import:Columns:&lt;file&gt;:&lt;field&gt; = "csv column"), because they must be checked against the real export; the
/// defaults are the snake_case field names the test fixture uses.
/// </summary>
public static class CsvSource
{
    public const string DownloadHint =
        "No input files. Download the 100K CSV export of the bank sample database from https://databasesample.com/datasets/bank-sample-database " +
        "and put customers.csv, accounts.csv, transactions.csv, loans.csv, loan_payments.csv and standing_orders.csv in {0}.";

    public static SourceData Load(string folder, IConfiguration columns)
    {
        if (!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, "*.csv").Any())
        {
            throw new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, DownloadHint, Path.GetFullPath(folder)));
        }

        var customers = Read(folder, "customers", columns, r => new SourceCustomer(r["id"], r["first_name"], r["last_name"], Date(r["date_of_birth"]),
            r["email"], r["phone"], r["address"]));
        var accounts = Read(folder, "accounts", columns, r => new SourceAccount(r["id"], r["customer_id"], r["type"]));
        var transactions = Read(folder, "transactions", columns, r => new SourceTransaction(r["id"], r["account_id"], Date(r["date"]), r["kind"].ToLowerInvariant(),
            Logic.Invariant.Decimal(r["amount"]), Optional(r["target_account_id"])));
        var loans = Read(folder, "loans", columns, r => new SourceLoan(r["id"], r["customer_id"], r["account_id"], r["type"], Logic.Invariant.Decimal(r["amount"]),
            int.Parse(r["term_months"], CultureInfo.InvariantCulture), Date(r["start_date"])));
        var payments = Read(folder, "loan_payments", columns, r => new SourceLoanPayment(r["id"], r["loan_id"], Date(r["date"]), Logic.Invariant.Decimal(r["amount"]),
            r["late"].Trim().ToLowerInvariant() is "1" or "true" or "yes"));
        var orders = Read(folder, "standing_orders", columns, r => new SourceStandingOrder(r["id"], r["account_id"], r["target_account_id"],
            Logic.Invariant.Decimal(r["amount"]), r["frequency"], Date(r["start_date"]), Optional(r["end_date"]) is { } end ? Date(end) : null));
        return new SourceData(customers, accounts, transactions, loans, payments, orders);
    }

    private static List<T> Read<T>(string folder, string file, IConfiguration columns, Func<Row, T> map)
    {
        var path = Path.Combine(folder, file + ".csv");
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var header = Split(lines[0]);
        return [.. lines.Skip(1).Select(line => map(new Row(header, Split(line), columns.GetSection(file))))];
    }

    /// <summary>One CSV line, with "quoted, values" and "" as an escaped quote.</summary>
    internal static List<string> Split(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted && ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static DateOnly Date(string value) => DateOnly.FromDateTime(DateTime.Parse(value, CultureInfo.InvariantCulture));

    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>A row read by field name; the field name maps to the CSV column through configuration.</summary>
    internal sealed class Row(IReadOnlyList<string> header, IReadOnlyList<string> values, IConfiguration mapping)
    {
        public string this[string field]
        {
            get
            {
                var column = mapping[field] ?? field;
                var index = header.ToList().FindIndex(h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));
                return index >= 0 && index < values.Count ? values[index].Trim() : "";
            }
        }
    }
}
