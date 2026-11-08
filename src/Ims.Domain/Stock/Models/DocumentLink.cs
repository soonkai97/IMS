namespace Ims.Domain;

/// <summary>The link of a document: its kind and the integer key of the order. <see cref="None"/> for every document that is not linked.</summary>
public readonly record struct DocumentLink(DocumentLinkKind Kind, int Id)
{
    public static DocumentLink None => default;

    public bool IsLinked => Kind != DocumentLinkKind.None;
}
