using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>At most <see cref="Limits.MaxAttachmentsPerOwner"/> files per owner, so this list is naturally bounded and needs no paging.</summary>
public class ListAttachmentsHandler(IAttachmentOwners owners, IReadRepository<Attachment> attachments, IReadRepository<User> users)
    : IQueryHandler<ListAttachmentsQuery, IReadOnlyList<AttachmentDto>>
{
    public async Task<IReadOnlyList<AttachmentDto>> HandleAsync(ListAttachmentsQuery q, CancellationToken ct)
    {
        await owners.For(q.OwnerType).EnsureCanReadAsync(q.OwnerId, ct);
        var rows = await attachments.ListAsync(new AttachmentsByOwnerSpec(q.OwnerType, q.OwnerId), ct);
        var uploaders = await attachments.ListAsync(new AttachmentUploadersSpec(q.OwnerType, q.OwnerId), ct);
        var names = uploaders.Count == 0 ? [] : (await users.ListAsync(new UsernamesByIdsSpec(uploaders.Select(u => u.UploadedBy).Distinct().ToList()), ct))
            .ToDictionary(u => u.Id, u => u.Username);
        var byId = uploaders.ToDictionary(u => u.Id, u => names.GetValueOrDefault(u.UploadedBy, ""));
        return rows.Select(r => r with { UploadedBy = byId.GetValueOrDefault(r.Id, "") }).ToList();
    }
}
