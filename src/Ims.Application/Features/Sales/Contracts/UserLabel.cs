namespace Ims.Application.Features.Sales;

/// <summary>A user as an order shows them: the integer key (to match), the public id, the user name and the display name. Internal to the application; never sent as JSON.</summary>
public record UserLabel(int Key, Guid PublicId, string Username, string DisplayName);
