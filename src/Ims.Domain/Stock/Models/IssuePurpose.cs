namespace Ims.Domain;

/// <summary>Why stock leaves on an issue. <see cref="Return"/> is only ever set by <see cref="StockDocument.CreateReturn"/> (a supplier return); the public create and save refuse it.</summary>
public enum IssuePurpose { Sale = 1, Use = 2, Scrap = 3, Return = 4 }
