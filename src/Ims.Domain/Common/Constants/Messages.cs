namespace Ims.Domain;

/// <summary>Text shown to users. Keep sentences short, say what to do next, and never expose internals.</summary>
public static class Messages
{
    // ----- Auth -----
    public const string InvalidCredentials = "Invalid username or password.";
    public const string AccountLocked = "Account is temporarily locked after too many failed attempts. Try again later.";
    public const string SessionExpired = "Session expired. Please sign in again.";
    public const string UsernameRequired = "Enter your username or email.";
    public const string PasswordRequired = "Enter your password.";
    public static readonly string PasswordTooShort = $"Password must be at least {Limits.PasswordMinLength} characters.";
    public const string CurrentPasswordWrong = "The current password is not correct.";
    public const string PasswordUnchanged = "Choose a password different from the current one.";

    public const string CannotLockOutSelf = "You cannot deactivate your own account or remove your own administrator role.";
    public const string LastAdminRequired = "At least one active administrator must remain.";
    public static string TooManyLines(int max) => $"A document can have at most {max} lines.";
    public const string QtyTooLarge = "Quantity is too large or has too many decimal places.";
    public const string ReasonTooLong = "The reason is too long.";

    // ----- Users -----
    public const string UserNotFound = "User not found.";
    public const string UserExists = "Username or email already exists.";
    public const string UsernameInvalid = "Username is required.";
    public const string EmailInvalid = "Enter a valid email address.";
    public const string UnknownRole = "Unknown role.";

    // ----- Catalogue -----
    public const string ProductNotFound = "Product not found.";
    public const string SkuRequired = "SKU is required (up to 64 characters).";
    public const string ProductNameRequired = "Product name is required.";
    public const string SafetyStockNegative = "Safety stock cannot be negative.";
    public const string BaseUomMissing = "Base unit does not exist.";
    public const string CategoryMissing = "Category does not exist.";
    public const string BaseUomLocked = "The base unit cannot change after stock movements exist.";
    public const string ConversionInvalid = "Conversions must convert into the base unit with a positive factor.";
    public const string CategoryNameRequired = "Category name is required.";
    public const string CategoryExists = "Category already exists.";
    public const string CategoryNotFound = "Category not found.";
    public const string UomCodeRequired = "Unit code is required.";
    public const string UomExists = "Unit code already exists.";
    public const string UomNotFound = "Unit not found.";
    public static string SkuExists(string sku) => $"SKU '{sku}' already exists.";
    public static string BarcodeTaken(string code) => $"Barcode '{code}' already belongs to another product.";
    public static string ProductMissing(int id) => $"Product {id} does not exist.";
    public static string ProductInactive(string sku) => $"Product {sku} is inactive and cannot be used on new documents.";
    public const string BarcodeInvalid = "Barcodes cannot be empty.";
    public const string ConversionMissing = "No unit conversion is defined for this product and unit.";
    public const string ConversionFactorInvalid = "Conversion factor must be positive.";

    // ----- Warehouses & partners -----
    public const string WarehouseNotFound = "Warehouse not found.";
    public const string WarehouseMissing = "Warehouse does not exist.";
    public const string WarehouseInactive = "Warehouse is inactive.";
    public const string WarehouseCodeRequired = "Warehouse code is required (up to 20 characters).";
    public const string WarehouseNameRequired = "Warehouse name is required.";
    public const string WarehouseCodeExists = "Warehouse code already exists.";
    public const string NoWarehouseAccess = "You do not have access to this warehouse.";
    public const string WarehouseCannotChange = "The warehouse of an existing document cannot change.";
    public const string PartnerNotFound = "Partner not found.";
    public const string PartnerMissing = "Partner does not exist.";
    public const string PartnerInactive = "Partner is inactive.";
    public const string PartnerNameRequired = "Name is required.";
    public static string PartnerWrongType(DocumentType doc, PartnerType expected) =>
        $"A {doc.ToString().ToLowerInvariant()} needs a {expected.ToString().ToLowerInvariant()}.";

    // ----- Settings & approval rules -----
    public const string RuleNotFound = "Rule not found.";
    public static string NumberAlreadyAssigned(string no) => $"Document number {no} is already assigned and cannot change.";
    public const string ThresholdNegative = "Thresholds cannot be negative.";
    public const string ThresholdRequired = "Set an amount threshold, a quantity threshold, or both.";

    // ----- Documents -----
    public const string DocumentNotFound = "Document not found.";
    public const string ChooseWarehouse = "Choose a warehouse.";
    public const string AddLine = "Add at least one line.";
    public const string ChooseProduct = "Choose a product on every line.";
    public const string QtyPositive = "Quantity must be greater than zero.";
    public const string CostNegative = "Unit cost cannot be negative.";
    public const string OnlyReceiptAndIssue = "Only receipts and issues are available in this version.";
    public const string RejectReasonRequired = "A rejection reason is required.";
    public const string ReverseReasonRequired = "A reversal reason is required.";
    public const string NeedsApprovalNotForStaff = "This document needs approval; warehouse staff can only post documents that do not.";
    public const string AboveApprovalLimit = "This document is above your approval limit. A higher role must approve it.";
    public const string NoLines = "A document needs at least one line.";
    public const string LineQtyPositive = "All line quantities must be greater than zero.";
    public const string SelfApproval = "You cannot approve a document you submitted.";
    public const string ApprovalNotNeeded = "This document does not need approval; it can be posted directly.";
    public const string ApprovalRequiredToPost = "This document requires approval before it can be posted.";
    public const string NegativeNeedsSupervisor = "Posting below zero stock needs a supervisor override.";
    public const string NegativeNeedsReason = "A reason is required to post below zero stock.";
    public static string NotEditable(string no, DocumentStatus status) => $"Document {no} is {status} and can no longer be edited.";
    public static string OnlyDraftCanSubmit(DocumentStatus status) => $"Only a Draft can be submitted (current: {status}).";
    public static string OnlySubmittedCanApprove(DocumentStatus status) => $"Only a Submitted document can be approved (current: {status}).";
    public static string OnlySubmittedCanReject(DocumentStatus status) => $"Only a Submitted document can be rejected (current: {status}).";
    public static string OnlyDraftOrSubmittedCanCancel(DocumentStatus status) => $"Only Draft or Submitted documents can be cancelled (current: {status}).";
    public static string CannotPostInStatus(DocumentStatus status) => $"A document in status {status} cannot be posted.";
    public const string OnlyPostedCanReverse = "Only a Posted document can be reversed.";
    public static string PostingNotSupported(DocumentType type) => $"Posting {type} documents is not supported yet.";

    // ----- Stock -----
    public const string ReceiveQtyPositive = "Receive quantity must be positive.";
    public const string IssueQtyPositive = "Issue quantity must be positive.";
    public static string InsufficientStock(int productId, int warehouseId, decimal available, decimal requested) =>
        $"Insufficient stock for product {productId} in warehouse {warehouseId}: available {available}, requested {requested}.";

    // ----- Attachments (files stored on disk, metadata in the database) -----
    public const string AttachmentNotFound = "File not found.";
    public const string FileEmpty = "The file is empty.";
    public const string FileTypeNotAllowed = "Only JPEG, PNG, WebP images and PDF documents can be uploaded.";
    public const string NotAllowed = "You do not have permission to do this.";
    public const string AttachmentDeleteOnlyDraft = "Files can only be removed while the document is a Draft.";
    public const string AttachmentOnCancelled = "Files cannot be added to a cancelled document.";
    public const string FileNameRequired = "A file name is required.";
    public const string FileStreamNotSeekable = "The uploaded file could not be read.";
    public static string FileTooLarge(int maxBytes) => $"The file is too large (maximum {maxBytes / (1024 * 1024)} MB).";
    public static string TooManyAttachments(int max) => $"At most {max} files can be attached.";

    // ----- Import -----
    public const string WorkbookTooLarge = "The workbook is too large once unpacked.";
    public const string PageTooDeep = "That page is too deep to list. Narrow the filter, or continue from the last row with afterId.";
    public const string FileHasActiveContent = "PDF files with scripts, launch actions or embedded files are not accepted.";
    public const string NotAWorkbook = "The file is not a valid .xlsx workbook.";
    public const string WorkbookEmpty = "The workbook has no sheets.";
    public const string UnknownTemplate = "Unknown template.";
    public static string ImportHasErrors(int rows) => $"{rows} row(s) have errors. Fix them and upload the file again; nothing was imported.";
    public static string TextTooLong(string field, int max) => $"{field} is too long (maximum {max} characters).";
    public const string UnitNotChanged = "Unit not changed: product already has stock movements.";
    public static string MissingColumns(IEnumerable<string> columns) =>
        $"Missing column(s): {string.Join(", ", columns)}. Download the template to see the expected columns.";
    public static string TooManyRows(int rows, int limit) => $"Too many rows ({rows}). The limit is {limit} per file.";
    public static string DuplicateSkuInFile(string sku) => $"SKU '{sku}' appears more than once in the file.";
    public static string BarcodeOwnedBy(string code, string sku) => $"Barcode '{code}' already belongs to {sku}.";
    public static string BarcodeDuplicateInFile(string code, string sku) => $"Barcode '{code}' is also used by {sku} in this file.";
    public static string UnknownSku(string sku) => $"Unknown or inactive SKU '{sku}'. Import products first.";
    public static string UnknownWarehouse(string code) => $"Unknown or inactive warehouse '{code}'.";
    public static string WarehouseNoAccess(string code) => $"You do not have access to warehouse '{code}'.";

    // ----- API / server -----
    public const string SignInRequired = "Sign in to continue.";
    public const string RouteNotFound = "The requested resource was not found.";
    public const string RequestNotValid = "The request is not valid.";
    public const string RequestRejectedGeneric = "The request could not be processed.";
    public const string RequestTimedOut = "The request took too long. Try again, or narrow the filters.";
    public const string ServiceBusy = "The system is busy. Please try again in a moment.";
    public const string RequestTimedOutTitle = "Request timed out";
    public const string ServiceUnavailableTitle = "Service unavailable";
    public const string ConcurrencyConflict = "The data changed while you were working on it. Reload and try again.";
    public static string DocumentChangedByAnother(string no) => $"{no} was changed by someone else since you opened it. Reload to see the latest version. Your changes were not saved.";
    public static string ServerError(string reference) => $"Something went wrong. Quote reference {reference} to support.";
    public const string RequestRejectedTitle = "Request rejected";
    public const string UnauthorizedTitle = "Unauthorized";
    public const string ForbiddenTitle = "Forbidden";
    public const string NotFoundTitle = "Not found";
    public const string ConflictTitle = "Conflict";
    public const string InsufficientStockTitle = "Insufficient stock";
    public const string BadRequestTitle = "Bad request";
    public const string ServerErrorTitle = "Server error";
}
