namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>What one step of an oracle sequence does: post a new receipt, post a new issue, or reverse a document posted by an earlier step.</summary>
internal enum StepKind { Receipt, Issue, Reverse }
