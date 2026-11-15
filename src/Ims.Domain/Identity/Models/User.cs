namespace Ims.Domain;

/// <summary>
/// A person who signs in (ADR roles-permissions): profile, one role, warehouse scope and the lockout counters of AUTH-01. It is an aggregate root (DDD-15): <see cref="MarkCreated"/> and <see cref="Update"/> raise an event
/// after a valid change, which a subscriber turns into the audit row. No event carries a password, a hash or a token.
/// </summary>
public sealed class User : AggregateRoot
{
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(Limits.LockoutMinutes);

    private User() { }

    public string Username { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    /// <summary>The role as a row, the only record of what the user is: its code is shown and audited, its permissions are the rights (the token claims). Loaded only where a query asks for it.</summary>
    public Role Role { get; private set; } = null!;
    public int RoleId { get; private set; }
    /// <summary>Sees every warehouse whatever the list of <see cref="Warehouses"/> says: a choice per person, not a trait of the role (ADR roles-permissions 2.6).</summary>
    public bool AllWarehouses { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int FailedLoginCount { get; private set; }
    public DateTime? LockoutUntil { get; private set; }

    private readonly List<UserWarehouse> _warehouses = [];

    /// <summary>Read-only view: change the data scope through <see cref="AssignWarehouses"/>.</summary>
    public IReadOnlyList<UserWarehouse> Warehouses => _warehouses;

    /// <param name="passwordHash">Already hashed by the infrastructure; the domain never sees a plain password.</param>
    public static User Create(string username, string email, string displayName, Role role, string passwordHash,
        IEnumerable<int>? warehouseIds = null, bool isActive = true, bool allWarehouses = false)
    {
        var u = new User { PasswordHash = passwordHash };
        u.UpdateProfile(username, email, displayName, role, isActive);
        u.AllWarehouses = allWarehouses;
        u.AssignWarehouses(warehouseIds ?? []);
        return u;
    }

    public void UpdateProfile(string username, string email, string displayName, Role role, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new DomainException(Messages.UsernameInvalid);
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException(Messages.EmailInvalid);
        // A role that is not a saved row (no key yet) cannot be pointed at: the foreign key would be the only thing to notice.
        if (role.Id <= 0) throw new DomainException(Messages.UnknownRole);
        if (!role.IsActive) throw new DomainException(Messages.RoleInactive);

        Username = username.Trim();
        Email = email.Trim();
        DisplayName = (displayName ?? "").Trim();
        Role = role;
        RoleId = role.Id;
        IsActive = isActive;
    }

    /// <summary>
    /// Announces that the user was created: raises <see cref="UserCreated"/>. The factory raises nothing on purpose. The use case writes the row first and announces after, so the row-level audit row of the new user
    /// (written by that first save) keeps the place before the business row that the subscriber stages at the second, the order the audit trail of a create has always had.
    /// </summary>
    public void MarkCreated() => Raise(new UserCreated(PublicId, Username, Role.Code, AllWarehouses));

    /// <summary>
    /// Saves the profile, role, active state, scope and warehouses of the user, and a new password hash when one is given, and raises <see cref="UserUpdated"/> with the state before and after (never the hash). A save
    /// that the profile rules refuse (a blank name, an unknown or inactive role) changes nothing and raises nothing. Raised whether or not anything changed: the audit row of a user save was always written.
    /// <paramref name="warehouseIds"/> is also what the event reports as the warehouses after the save, in the order given.
    /// </summary>
    public void Update(string username, string email, string displayName, Role role, bool isActive, bool allWarehouses, IReadOnlyList<int> warehouseIds, string? passwordHash)
    {
        var before = new UserSummary(Username, Role.Code, AllWarehouses, IsActive, _warehouses.Select(w => w.WarehouseId).ToList());
        UpdateProfile(username, email, displayName, role, isActive);
        if (passwordHash is not null) ChangePasswordHash(passwordHash);
        SetAllWarehouses(allWarehouses);
        AssignWarehouses(warehouseIds);
        Raise(new UserUpdated(PublicId, before, new UserSummary(Username, Role.Code, AllWarehouses, IsActive, warehouseIds.ToList())));
    }

    public void ChangePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    /// <summary>Data scope (AUTH-02): whether the user sees every warehouse, whatever their list says.</summary>
    public void SetAllWarehouses(bool allWarehouses) => AllWarehouses = allWarehouses;

    /// <summary>Data scope (AUTH-02): makes the user's warehouses exactly <paramref name="warehouseIds"/>.</summary>
    public void AssignWarehouses(IEnumerable<int> warehouseIds)
    {
        var wanted = warehouseIds.Distinct().ToHashSet();
        _warehouses.RemoveAll(w => !wanted.Contains(w.WarehouseId));
        foreach (var id in wanted.Where(id => _warehouses.All(w => w.WarehouseId != id)))
            _warehouses.Add(UserWarehouse.For(id));
    }

    /// <summary>An active person whose role administers the system (<see cref="Role.Administers"/>); the system must always keep at least one. Needs the role and its grants loaded.</summary>
    public bool IsAdministrator => IsActive && Role.Administers;

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
