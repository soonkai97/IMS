using Ims.Domain;
namespace Ims.Application.Features.Users;

public record UserFilter(string? Search, string? Role, bool? Active, int Page = 1, int PageSize = Limits.DefaultListPageSize);
