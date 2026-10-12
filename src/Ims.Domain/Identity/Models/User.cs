namespace Ims.Domain;

public class User : PublicEntity
{
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(Limits.LockoutMinutes);

    private User() { }

    public string Username { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public string Role { get; private set; } = Roles.WarehouseStaff;
    public bool IsActive { get; private set; } = true;
    public int FailedLoginCount { get; private set; }
    public DateTime? LockoutUntil { get; private set; }

    private readonly List<UserWarehouse> _warehouses = [];

    /// <summary>Read-only view: change the data scope through <see cref="AssignWarehouses"/>.</summary>
    public IReadOnlyList<UserWarehouse> Warehouses => _warehouses;

    /// <param name="passwordHash">Already hashed by the infrastructure; the domain never sees a plain password.</param>
    public static User Create(string username, string email, string displayName, string role, string passwordHash,
        IEnumerable<int>? warehouseIds = null, bool isActive = true)
    {
        var u = new User { PasswordHash = passwordHash };
        u.UpdateProfile(username, email, displayName, role, isActive);
        u.AssignWarehouses(warehouseIds ?? []);
        return u;
    }

    public void UpdateProfile(string username, string email, string displayName, string role, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new DomainException(Messages.UsernameInvalid);
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException(Messages.EmailInvalid);
        if (!Roles.All.Contains(role)) throw new DomainException(Messages.UnknownRole);

        Username = username.Trim();
        Email = email.Trim();
        DisplayName = (displayName ?? "").Trim();
        Role = role;
        IsActive = isActive;
    }

    public void ChangePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    /// <summary>Data scope (AUTH-02): makes the user's warehouses exactly <paramref name="warehouseIds"/>.</summary>
    public void AssignWarehouses(IEnumerable<int> warehouseIds)
    {
        var wanted = warehouseIds.Distinct().ToHashSet();
        _warehouses.RemoveAll(w => !wanted.Contains(w.WarehouseId));
        foreach (var id in wanted.Where(id => _warehouses.All(w => w.WarehouseId != id)))
            _warehouses.Add(UserWarehouse.For(id));
    }

    public bool IsLockedOut(DateTime utcNow) => LockoutUntil is { } until && until > utcNow;

    /// <summary>AUTH-01: the fifth wrong password in a row locks the account for a while.</summary>
    public void RegisterFailedLogin(DateTime utcNow)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= Limits.MaxFailedLogins)
        {
            LockoutUntil = utcNow + LockoutDuration;
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
    }
}
