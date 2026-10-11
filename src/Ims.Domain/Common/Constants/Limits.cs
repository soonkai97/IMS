namespace Ims.Domain;

/// <summary>Numeric limits and defaults.</summary>
public static class Limits
{
    // Security
    public const int MaxFailedLogins = 5;
    public const int LockoutMinutes = 15;
    public const int PasswordMinLength = 12;
    public const int RefreshTokenDays = 7;
    public const int AccessTokenMinutes = 15;
    public const int ClockSkewSeconds = 30;
    public const int JwtKeyMinLength = 32;
    public const int JwtKeyGeneratedBytes = 48;
    public const int RefreshTokenBytes = 48;
    public const int LoginRateLimitPerMinute = 20;
    public const int GlobalRateLimitPerMinute = 600;

    // Time (BR-10: stored in UTC, shown in Asia/Kuala_Lumpur, which has no DST)
    public const int MalaysiaUtcOffsetHours = 8;

    // Concurrency (BR-12)
    public const int MaxRetryAttempts = 5;
    /// <summary>How long a posting waits for another posting of the same product to finish before giving up (and being retried).</summary>
    public const int StockLockTimeoutMs = 10_000;
    /// <summary>Back-off before a retry = attempt x (base + random jitter), so colliding requests do not retry in lockstep.</summary>
    public const int RetryBaseDelayMs = 25;
    public const int RetryJitterMs = 50;
    public const int DbCommandTimeoutSeconds = 60;

    // Timeouts. Order matters: the HTTP timeout fires first and cancels the running SQL command through the request token.
    public const int RequestTimeoutSeconds = 30;
    public const int LongRequestTimeoutSeconds = 120;
    public const int KestrelHeadersTimeoutSeconds = 15;
    public const int KeepAliveTimeoutSeconds = 120;
    public const int RateLimitWindowMinutes = 1;

    // Request and file size caps
    public const int MaxAttachmentBytes = 10 * 1024 * 1024;
    public const int MaxRequestBodyBytes = MaxAttachmentBytes + 2 * 1024 * 1024;
    /// <summary>Every JSON endpoint: requests are small. Uploads and imports raise their own limit with [RequestSizeLimit].</summary>
    public const int MaxJsonBodyBytes = 1024 * 1024;
    /// <summary>An .xlsx is a zip: its expanded size is capped so a small file cannot expand to gigabytes (zip bomb).</summary>
    public const int MaxImportUncompressedBytes = 50 * 1024 * 1024;

    // Paging and export
    public const int DefaultPageSize = 25;
    public const int DefaultListPageSize = 50;
    public const int MaxPageSize = 200;
    /// <summary>Hard cap for small "lookup" lists (units, categories, warehouses, rules) that are returned unpaged.</summary>
    public const int MaxLookupRows = 1000;
    public const int MaxStockPageSize = 500;
    public const int MaxExportRows = 50_000;

    // Import (MD-05)
    public const int MaxImportRows = 5000;
    public const int MaxImportFileBytes = 5 * 1024 * 1024;

    // Field lengths
    public const int SkuMaxLength = 64;
    public const int BarcodeMaxLength = 64;
    public const int NameMaxLength = 300;
    public const int ShortNameMaxLength = 200;
    public const int CodeMaxLength = 20;
    public const int UsernameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int RoleMaxLength = 32;
    public const int RemarkMaxLength = 500;
    /// <summary>Shortest reason (after trimming) accepted for a reject, a reversal or a negative-stock override (UX 6.4).</summary>
    public const int ReasonMinLength = 5;
    public const int DocumentNoMaxLength = 40;
    public const int DocumentPrefixMaxLength = 8;
    public const int AuditTextMaxLength = 100;
    public const int UomNameMaxLength = 100;
    public const int SettingKeyMaxLength = 100;
    public const int PasswordMaxLength = 128;
    public const int MaxAttachmentsPerOwner = 20;
    public const int AttachmentNameMaxLength = 200;
    public const int AttachmentPathMaxLength = 300;
    public const int ContentTypeMaxLength = 100;
    public const int Sha256HexLength = 64;
    public const int FileHeaderBytes = 16;
    public const int MaxPageNumber = 100_000;
    /// <summary>OFFSET paging cost grows with depth: rows beyond this must be reached with a cursor (afterId) or by narrowing the filter.</summary>
    public const int MaxOffsetRows = 10_000;
    public const int MaxDocumentLines = 500;
    public const decimal MaxQuantity = 1_000_000_000m;
    /// <summary>The largest amount of one document line (it is stored as <c>decimal(18,2)</c>): just under 1e15. Above it the request is a 400, never a database overflow.</summary>
    public const decimal MaxLineAmount = 999_999_999_999_999m;
    public const int QuantityScale = 4;
    public const int QuantityPrecision = 18;
    public const int SearchMaxLength = 100;
    public const int TokenHashMaxLength = 128;
    public const int SettingValueMaxLength = 500;

    // Settings
    public const int DefaultLowStockCooldownHours = 24;
    public const int MaxLowStockCooldownHours = 24 * 30;

    // Decimal precision
    /// <summary>
    /// Decimal places a cost per base unit and the moving average keep INSIDE the system (ledger, document lines, balances). Wider than the 2 places of
    /// any amount a person sees, so a unit conversion (a carton price spread over 12 pieces) and repeated blending drift far less than at 6 places. These
    /// are internal numbers, not money: an amount that reaches a person follows the money rule (2 places).
    /// </summary>
    public const int CostDecimals = 10;

    /// <summary>How internal costs are rounded: half away from zero, the same mode as money.</summary>
    public const MidpointRounding CostRounding = MidpointRounding.AwayFromZero;

    /// <summary>The largest unit cost a person may enter (per entered unit). Above it the request is a 400, never a database overflow.</summary>
    public const decimal MaxUnitCost = 1_000_000_000m;

    /// <summary>
    /// The largest cost per base unit or average the database column can hold (<c>decimal(22,10)</c> has 12 integer digits). A cost entered per carton and spread
    /// over a fraction of a unit can exceed it even when the entered cost is below <see cref="MaxUnitCost"/>: that is a domain error too.
    /// </summary>
    public const decimal MaxCostPerBaseUnit = 999_999_999_999m;
}
