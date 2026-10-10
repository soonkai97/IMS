using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>A live (not deleted) attachment by id. Tracked, so it can be changed.</summary>
public sealed class AttachmentByIdSpec : Specification<Attachment>
{
    public AttachmentByIdSpec(int id) => Query.Where(a => a.Id == id);
}
