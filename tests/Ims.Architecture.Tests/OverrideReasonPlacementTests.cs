using System.Reflection;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>
/// BR-03: the reason for letting stock go below zero is given by the approver, when approving, and stored on the document.
/// The poster never supplies it, so no posting input may carry free text.
/// </summary>
public class OverrideReasonPlacementTests
{
    [Fact]
    public void PostDocumentCommand_CarriesNoReason_OnlyTheIdAndTheVersion()
    {
        var parameters = typeof(PostDocumentCommand).GetConstructors().Single().GetParameters().Select(p => p.Name!);

        Assert.Equal([nameof(PostDocumentCommand.Id), nameof(PostDocumentCommand.IfMatch)], parameters);
    }

    [Fact]
    public void PostingContext_TakesNoReason_ItReadsTheOneRecordedOnTheDocument()
    {
        var stringParameters = typeof(PostingContext).GetConstructors().SelectMany(c => c.GetParameters()).Where(p => p.ParameterType == typeof(string));

        Assert.Empty(stringParameters);
        Assert.Null(typeof(PostingContext).GetProperty(nameof(PostingContext.OverrideReason))!.SetMethod);
    }

    [Fact]
    public void ApproveDocumentCommand_CarriesTheReasonAndHasAValidator()
    {
        Assert.NotNull(typeof(ApproveRequest).GetProperty(nameof(ApproveRequest.NegativeStockOverrideReason)));
        Assert.Contains(Layers.TypesOf(Layers.Application), t => t == typeof(ApproveDocumentValidator));
    }

    [Fact]
    public void StockDocument_TakesTheReasonOnlyWhenApproving()
    {
        var takers = typeof(StockDocument).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p => p.Name is "overrideReason" or "negativeStockOverrideReason"))
            .Select(m => m.Name);

        Assert.Equal([nameof(StockDocument.Approve)], takers);
    }
}
