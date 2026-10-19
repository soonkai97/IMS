namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>How far a document got before the step tries to post it.</summary>
internal enum ApprovalState
{
    /// <summary>Submitted, no approval needed: it can post directly.</summary>
    NotNeeded,

    /// <summary>Submitted and waiting for an approver: posting must be refused.</summary>
    Pending,

    /// <summary>Approved, with no override reason recorded.</summary>
    Approved,

    /// <summary>Submitted as an override and approved with the step's reason (BR-03).</summary>
    Override
}
