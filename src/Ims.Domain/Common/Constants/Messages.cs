namespace Ims.Domain;

/// <summary>Text shown to users. Keep sentences short, say what to do next, and never expose internals.</summary>
public static class Messages
{
    // ----- Auth -----
    public const string InvalidCredentials = "Invalid username or password.";
    public const string AccountLocked = "Account is temporarily locked after too many failed attempts. Try again later.";
    /// <summary>UX E7. Only shown to someone who gave the right password, so it does not reveal that an account exists.</summary>
    public static string AccountLockedFor(int minutes) =>
        minutes == 1 ? "Too many failed attempts. Try again in 1 minute." : $"Too many failed attempts. Try again in {minutes} minutes.";
    public static string TooManyRequests(int seconds) =>
        seconds <= 1 ? "Too many requests. Try again in a moment." : $"Too many requests. Try again in {seconds} seconds.";
    public static readonly string SearchTooShort = $"Type at least {Limits.SearchMinTextLength} characters.";
    public static readonly string SearchTooLong = $"Search text can be at most {Limits.SearchMaxLength} characters.";
    public const string DateRangeInvalid = "The 'from' date is after the 'to' date.";
    public const string IfMatchRequired = "Send the document's version in the If-Match header (the ETag you last received), so a change made by someone else is not overwritten.";
    public const string PurposeRequired = "Choose a purpose for the issue.";
    public const string UsernameCharacters = "Username can contain letters, digits and . _ - @ + only (no spaces).";
    public const string SessionExpired = "Session expired. Please sign in again.";
    public const string UsernameRequired = "Enter your username or email.";
    public const string PasswordRequired = "Enter your password.";
    public static readonly string PasswordTooShort = $"Password must be at least {Limits.PasswordMinLength} characters.";
    public const string CurrentPasswordWrong = "The current password is not correct.";
    public const string PasswordUnchanged = "Choose a password different from the current one.";
    public const string CrossSiteRequestRefused = "This request did not come from a trusted application.";

    public const string CannotLockOutSelf = "You cannot deactivate your own account or remove your own administrator role.";
    public const string LastAdminRequired = "At least one active administrator must remain.";
    public static string TooManyLines(int max) => $"A document can have at most {max} lines.";
    public const string QtyTooLarge = "Quantity is too large or has too many decimal places.";
    public const string ReasonTooLong = "The reason is too long.";
    public static readonly string ReasonTooShort = $"Enter a reason of at least {Limits.ReasonMinLength} characters.";

    // ----- Users -----
    public const string UserNotFound = "User not found.";
    public const string UserExists = "Username or email already exists.";
    public const string UsernameInvalid = "Username is required.";
    public const string EmailInvalid = "Enter a valid email address.";
    public const string UnknownRole = "Unknown role.";
    public const string RoleInactive = "This role is not active. Choose another role.";
    public const string AllWarehousesNotGrantable = "Only a user who sees every warehouse can let someone else see every warehouse.";
    public const string UserBeyondYourAccess = "You cannot change a user who has more access than you.";
    public const string RoleBeyondYourAccess = "You can only give a role whose permissions you hold yourself.";
    public const string WarehouseOutsideYourScope = "You can only assign warehouses you can reach yourself.";
    public const string UnknownApproverTier = "Unknown approver tier.";
    public const string ApproverTierRequired = "Choose the approver tier (standard or large).";
    public static readonly string RoleCodeInvalid = $"A role needs a code of up to {Limits.RoleMaxLength} characters.";
    public static readonly string RoleNameInvalid = $"A role name needs {Limits.RoleNameMinLength} to {Limits.RoleNameMaxLength} characters.";
    public static readonly string RoleDescriptionTooLong = $"A role description can have at most {Limits.RoleDescriptionMaxLength} characters.";
    public static string UnknownPermission(string code) => $"Unknown permission '{code}'.";

    // ----- Roles -----
    public const string RoleNotFound = "Role not found.";
    public static string RoleNameTaken(string name) => $"A role named '{name}' already exists.";
    public static string DuplicatePermission(string code) => $"Permission '{code}' is listed more than once.";
    public static string PermissionNeedsAnother(string code, string required) => $"'{code}' also needs '{required}'.";
    public const string RoleTextNotPlain = "A role name and description cannot contain control or direction-changing characters.";
    public const string TooManyPermissions = "A role cannot hold more permissions than the catalog has.";
    public static string MorePermissionsRefused(int count) => $"{count} more permission(s) are not valid either.";
    public const string RoleCopyAndPermissions = "Give either a role to copy or a list of permissions, not both.";
    public const string RoleCopySourceMissing = "The role to copy does not exist.";
    public const string GrantBeyondYourAccess = "You can only grant permissions you hold yourself.";
    public const string RoleEditBeyondYourAccess = "You cannot change a role that holds more permissions than you.";
    public const string RoleHeldBeyondYourScope = "You cannot change a role that is held by a user who sees more warehouses than you.";
    public const string CannotChangeOwnRole = "You cannot change your own role. Ask another administrator.";
    public const string SystemRoleProtected = "System roles cannot be deleted or deactivated.";
    public const string AdminRoleLocked = "The Administrator role always holds every permission and cannot be changed.";
    public const string AdminCoreIsAdministratorOnly = "Only the Administrator role holds the whole administration core (users and roles, read and manage).";
    public static string RoleInUse(int users) => $"{users} user(s) still have this role. Move them to another role first.";
    public const string RoleChangedByOther = "The role was changed by someone else. Reload it and try again.";

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
    public static string ProductMissing(Guid id) => $"Product {id} does not exist.";
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
    public const string WarehouseCannotChange = "The warehouse of an existing document cannot change.";
    public const string PartnerNotFound = "Partner not found.";
    public const string PartnerMissing = "Partner does not exist.";
    public const string PartnerInactive = "Partner is inactive.";
    public const string PartnerNameRequired = "Name is required.";
    public static string PartnerExists(PartnerType type, string name) => $"A {type.ToString().ToLowerInvariant()} named '{name}' already exists.";
    public static string PartnerWrongType(DocumentType doc, PartnerType expected) =>
        $"A {doc.ToString().ToLowerInvariant()} needs a {expected.ToString().ToLowerInvariant()}.";

    // ----- Settings & approval rules -----
    public const string RuleNotFound = "Rule not found.";
    public static string NumberAlreadyAssigned(string no) => $"Document number {no} is already assigned and cannot change.";
    public const string ThresholdNegative = "Thresholds cannot be negative.";
    public const string AmountTooPrecise = "An amount can have at most 2 decimal places.";
    public const string LineAmountTooLarge = "The amount of a line cannot be more than 999,999,999,999,999.";
    public const string ThresholdRequired = "Set an amount threshold, a quantity threshold, or both.";

    // ----- Documents -----
    public const string DocumentNotFound = "Document not found.";
    public const string ChooseWarehouse = "Choose a warehouse.";
    public const string AddLine = "Add at least one line.";
    public const string ChooseProduct = "Choose a product on every line.";
    public const string QtyPositive = "Quantity must be greater than zero.";
    public const string CostTooLarge = "Unit cost cannot be more than 1,000,000,000.";
    public const string CostTooLargeForUnit = "Unit cost is too large for this unit of measure.";
    public const string CostNegative = "Unit cost cannot be negative.";
    public const string CostTooPrecise = "Unit cost can have at most 2 decimal places.";
    public static string RestrictedSortKey(string key) => $"Sorting by {key.ToLowerInvariant()} is not available to your role.";
    public const string OnlyReceiptAndIssue = "Only receipts and issues are available in this version.";
    public const string RejectReasonRequired = "A rejection reason is required.";
    public const string ReverseReasonRequired = "A reversal reason is required.";
    public const string OwnDocumentsOnly = "You can only change documents you created.";
    public const string OwnSubmissionsOnly = "You can only cancel documents you submitted.";
    public const string OwnRecallOnly = "Only the person who submitted a document can take it back to draft.";
    public const string OnlyReviewerCanCancelApproved = "Only an approver for this document can cancel it after approval.";
    public const string CancelApprovedReasonRequired = "A reason is required to cancel an approved document.";
    public const string SelfRejection = "You cannot reject a document you submitted.";
    public const string OwnerReviewsLargeOnly = "Owners review only large documents. A supervisor reviews this one.";
    public const string NeedsApprovalNotForStaff = "This document needs approval; warehouse staff can only post documents that do not.";
    public const string AboveApprovalLimit = "This document is above your approval limit. A higher role must approve it.";
    public const string NoLines = "A document needs at least one line.";
    public const string LineQtyPositive = "All line quantities must be greater than zero.";
    public const string SelfApproval = "You cannot approve a document you submitted.";
    public const string ApprovalNotNeeded = "This document does not need approval; it can be posted directly.";
    public const string ApprovalRequiredToPost = "This document requires approval before it can be posted.";
    public const string NegativeNeedsSupervisor = "Posting below zero stock needs a supervisor override.";
    public const string NegativeNeedsReason = "Posting below zero stock needs an override reason, which the approver gives when approving.";
    public const string OverrideReasonRequired = "A reason is required to approve a document that takes stock below zero.";
    public static string NotEditable(string no, DocumentStatus status) => $"Document {no} is {status} and can no longer be edited.";
    public static string OnlyDraftCanSubmit(DocumentStatus status) => $"Only a Draft can be submitted (current: {status}).";
    public static string OnlySubmittedCanApprove(DocumentStatus status) => $"Only a Submitted document can be approved (current: {status}).";
    public static string OnlySubmittedCanReject(DocumentStatus status) => $"Only a Submitted document can be rejected (current: {status}).";
    public static string OnlySubmittedCanRecall(string no, DocumentStatus status) => $"{no} is {status} and can't be taken back to draft.";
    public static string OnlyDraftSubmittedOrApprovedCanCancel(DocumentStatus status) => $"Only Draft, Submitted or Approved documents can be cancelled (current: {status}).";
    public static string CannotPostInStatus(DocumentStatus status) => $"A document in status {status} cannot be posted.";
    public const string OnlyPostedCanReverse = "Only a Posted document can be reversed.";

    public static string ReversalBlocked(string docNo, IReadOnlyList<ReversalShortage> items)
    {
        const string nextStep = "Reverse or adjust the later issues first, or ask an Admin.";
        var first = items[0];
        return items.Count == 1
            ? $"Can't reverse {docNo}. It would make {first.Sku} negative ({Quantity(first.Balance)} {first.UomCode}). {nextStep}"
            : $"Can't reverse {docNo}. It would make {items.Count} products negative: {string.Join(", ", items.Select(i => $"{i.Sku} ({Quantity(i.Balance)} {i.UomCode})"))}. {nextStep}";
    }

    /// <summary>A quantity inside a sentence: invariant culture, no trailing zeros ("-3", "12.5").</summary>
    private static string Quantity(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    public static string PostingNotSupported(DocumentType type) => $"Posting {type} documents is not supported yet.";

    // ----- Stock -----
    public const string ReceiveQtyPositive = "Receive quantity must be positive.";
    public const string IssueQtyPositive = "Issue quantity must be positive.";
    public static string InsufficientStock(int productId, int warehouseId, decimal available, decimal requested) =>
        $"Insufficient stock for product {productId} in warehouse {warehouseId}: available {available}, requested {requested}.";

    /// <summary>The conflict for a product or warehouse that cannot be named (it was soft-deleted): by public id, never by the integer keys.</summary>
    public static string InsufficientStockUnnamed(Guid productPublicId, Guid warehousePublicId, decimal available, decimal requested) =>
        $"Insufficient stock for product {productPublicId} in warehouse {warehousePublicId}: available {Quantity(available)}, requested {Quantity(requested)}.";

    /// <summary>The same conflict in words a person recognises: SKU, warehouse name and unit instead of ids (NV-06 / PS-01).</summary>
    public static string InsufficientStockNamed(string sku, string warehouseName, string uomCode, decimal available, decimal requested) =>
        $"Not enough stock for {sku} in {warehouseName}: {Quantity(available)} {uomCode} available, {Quantity(requested)} needed.";

    // ----- Attachments (files stored on disk, metadata in the database) -----
    public const string AttachmentNotFound = "File not found.";
    public const string AttachmentOwnerRequired = "Choose what the file belongs to.";
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
    public const string PageTooDeep = "That page is too deep to list. Narrow the filter, or continue from the last row with the cursor.";
    public const string CursorInvalid = "The cursor is not valid. Send the nextCursor of the previous page unchanged.";
    public const string FileHasActiveContent = "PDF files with scripts, launch actions or embedded files are not accepted.";
    public const string FileNotAValidImage = "The image is damaged or not a supported image. Choose another file.";
    public const string FileRejectedByScanner = "The file was rejected by the security check and was not stored.";
    public const string NotAWorkbook = "The file is not a valid .xlsx workbook.";
    public const string WorkbookEmpty = "The workbook has no sheets.";
    public const string UnknownTemplate = "Unknown template.";
    public static string ImportHasErrors(int rows) => $"{rows} row(s) have errors. Fix them and upload the file again; nothing was imported.";
    public static string TextTooLong(string field, int max) => $"{field} is too long (maximum {max} characters).";
    public const string UnitNotChanged = "Unit not changed: product already has stock movements.";
    public static string MissingColumns(IEnumerable<string> columns) =>
        $"Missing column(s): {string.Join(", ", columns)}. Download the template to see the expected columns.";
    public static string TooManyRows(int rows, int limit) => $"Too many rows ({rows}). The limit is {limit} per file.";
    public static string DuplicateSkuInFile(string sku, int firstRow) => $"SKU '{sku}' also appears in row {firstRow}.";
    public static string ImportCategoryMissing(string name) => $"Category '{name}' doesn't exist. Add it first or fix the name.";
    public static string ImportUnitMissing(string unit) => $"Unit '{unit}' doesn't exist. Use the code or name of an existing unit, or add it first.";
    public static string NotANumber(string column, string value) => $"{column} '{value}' is not a number.";
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
    public const string PublicIdInUse = "The record could not be saved because its identifier was already taken. Try again.";
    public static string DocumentChangedByAnother(string no) => $"{no} was changed by someone else since you opened it. Reload to see the latest version. Your changes were not saved.";
    public static string ServerError(string reference) => $"Something went wrong. Quote reference {reference} to support.";
    public const string RequestRejectedTitle = "Request rejected";
    public const string UnauthorizedTitle = "Unauthorized";
    public const string ForbiddenTitle = "Forbidden";
    public const string NotFoundTitle = "Not found";
    public const string ConflictTitle = "Conflict";
    public const string InsufficientStockTitle = "Insufficient stock";
    public const string ReversalBlockedTitle = "Reversal blocked";
    public const string PreconditionRequiredTitle = "Precondition required";
    public const string TooManyRequestsTitle = "Too many requests";
    public const string BadRequestTitle = "Bad request";
    public const string ServerErrorTitle = "Server error";
}
