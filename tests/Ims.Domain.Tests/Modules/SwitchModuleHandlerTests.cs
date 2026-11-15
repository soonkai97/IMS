using Ardalis.Specification;
using Ims.Application;
using Ims.Application.Features.Audit;
using Ims.Application.Features.Modules;
using Ims.Domain.Tests.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Modules;

/// <summary>
/// DDD-14, characterization: <c>SwitchModuleHandler</c> as it is on <c>main</c> before its decision moves into <c>ModuleSwitchPolicy</c>, the audit row into a subscriber of <c>ModuleSwitched</c> and the cache
/// <c>Invalidate</c> into an after-commit handler. Green before the change and green after it, with this file unchanged: the same exception type and sentence for every input, the same audit row (raw JSON,
/// actor, time), the same settings row written, and the same ORDER of the steps (the <c>modules</c> lock, the read of the row, the read of the other modules, the checks, the roles count, the audit row, the save,
/// the cache invalidation), and nothing asked once a refusal is known. The 400, 404 and 409 mapping is the one of the exception types; <c>Ims.Api.Tests</c> (<c>ModulesApiTests</c>, <c>ModuleDisableCheckTests</c>)
/// prove the status on the wire. The world is built from the handler's own constructor, whatever it asks for, so the file does not change when the audit and the invalidation leave the handler: the stand-in
/// unit of work does what the real one does for events (in-transaction handlers at a save, after-commit handlers when the outermost transaction ends), and does nothing when no event is raised.
/// </summary>
public class SwitchModuleHandlerTests
{
    private const string Admin = "admin";

    [Fact]
    public async Task EveryModuleRowOthersStateReasonAndCheck_GivesTheSameAnswerThatTheTableWrites()
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in ModuleSwitchTable.All())
        {
            count++;
            var world = new World(c);

            var (result, thrown) = await world.RunAsync();

            var expected = ModuleSwitchTable.Expected(c);
            var got = world.OutcomeOf(result, thrown);
            if (got != expected) wrong.Add($"{c}: expected {expected}, got {got} ({thrown?.GetType().Name}: {thrown?.Message})");
            else if (thrown is not null && thrown.Message != ModuleSwitchTable.ExpectedMessage(c)) wrong.Add($"{c}: wrong sentence '{thrown.Message}'");
            else if (thrown is not null && thrown.GetType() != ExpectedException(expected)) wrong.Add($"{c}: wrong exception {thrown.GetType().Name}");

            var expectedJournal = ExpectedJournal(c, expected);
            if (!expectedJournal.SequenceEqual(world.Journal)) wrong.Add($"{c}: expected steps [{string.Join(" | ", expectedJournal)}], got [{string.Join(" | ", world.Journal)}]");

            var expectedState = expected == SwitchOutcome.Switched ? c.Wanted : c.Before;
            if (world.RowIsOn() != expectedState) wrong.Add($"{c}: the row says {world.RowIsOn()}, expected {expectedState}");
            if (expected != SwitchOutcome.Switched && world.Settings.Added.Count > 0) wrong.Add($"{c}: a row was added though nothing was switched");
            if (expected != SwitchOutcome.Switched && world.Logs.Added.Count > 0) wrong.Add($"{c}: an audit row was written though nothing was switched");
            if (expected == SwitchOutcome.Switched && world.Logs.Added.Count != 1) wrong.Add($"{c}: {world.Logs.Added.Count} audit rows, expected 1");
        }

        Assert.True(count > 1000, $"the table is the whole grid ({count} cases)");
        Assert.Equal(Enum.GetValues<SwitchOutcome>().Length, ModuleSwitchTable.All().Select(ModuleSwitchTable.Expected).Distinct().Count()); // the grid reaches every outcome
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    private static Type ExpectedException(SwitchOutcome outcome) => outcome switch
    {
        SwitchOutcome.ReasonRequired => typeof(DomainException),
        _ => typeof(ConflictException),
    };

    /// <summary>The steps of a request, written from the rules: what is asked, in order, and nothing once a refusal is known.</summary>
    private static List<string> ExpectedJournal(ModuleSwitchTable.Case c, SwitchOutcome outcome)
    {
        var steps = new List<string> { World.Lock, World.ReadRow };
        if (outcome == SwitchOutcome.Unchanged) return [.. steps, World.Invalidate];
        if (outcome == SwitchOutcome.ReasonRequired) return steps;

        steps.Add(World.ReadOthers);
        if (c.Wanted)
        {
            if (outcome == SwitchOutcome.NeedsOtherModules) return steps;
            steps.Add($"ready {c.Code}");
            if (outcome == SwitchOutcome.NotReady) return steps;
            steps.Add(World.CountRoles);
        }
        else
        {
            if (outcome == SwitchOutcome.NeededByOtherModules) return steps;
            steps.Add($"orphan {c.Code}");
            if (outcome == SwitchOutcome.HasOpenData) return steps;
        }
        steps.AddRange([$"audit {(c.Wanted ? AuditActions.ModuleEnabled : AuditActions.ModuleDisabled)}", World.Save, World.Invalidate]);
        return steps;
    }

    [Fact]
    public async Task TheSentencesAreTheOnesTheApiAnswersWith()
    {
        Assert.Equal("A reason is required to turn a module on or off.", Messages.ModuleReasonRequired);
        Assert.Equal("Module not found.", Messages.ModuleNotFound);
        Assert.Equal("The Returns module cannot be turned on until Sales and Purchasing are on.", Messages.ModuleNeedsOthers("Returns", ["Sales", "Purchasing"]));
        Assert.Equal("The Returns module cannot be turned on until Sales is on.", Messages.ModuleNeedsOthers("Returns", ["Sales"]));
        Assert.Equal("The Sales module cannot be turned off while Returns is on. Turn it off first.", Messages.ModuleIsNeededBy("Sales", ["Returns"]));
        Assert.Equal("The Purchasing module cannot be turned off while 3 purchase orders are still open. Finish or cancel them first.", Messages.ModuleHasOpenData("Purchasing", "3 purchase orders are still open"));

        var needs = await new World(Case(ModuleCodes.Returns, OwnRow.Off, [ModuleCodes.Sales], wanted: true)).RunAsync();
        var neededBy = await new World(Case(ModuleCodes.Sales, OwnRow.On, [ModuleCodes.Returns], wanted: false)).RunAsync();
        var reason = await new World(Case(ModuleCodes.Sales, OwnRow.Off, [], wanted: true, reason: "abcd")).RunAsync();

        Assert.IsType<ConflictException>(needs.Thrown);
        Assert.Equal("The Returns module cannot be turned on until Purchasing is on.", needs.Thrown!.Message);
        Assert.IsType<ConflictException>(neededBy.Thrown);
        Assert.Equal("The Sales module cannot be turned off while Returns is on. Turn it off first.", neededBy.Thrown!.Message);
        Assert.IsType<DomainException>(reason.Thrown);
        Assert.Equal(Messages.ModuleReasonRequired, reason.Thrown!.Message);
    }

    [Fact]
    public async Task AnUnknownCode_IsNotFound_BeforeAnythingIsLockedReadOrWritten()
    {
        foreach (var code in new[] { "nope", "Purchasing", "", "returns " })
        {
            var world = new World(Case(ModuleCodes.Purchasing, OwnRow.Off, [], wanted: true), code: code);

            var (_, thrown) = await world.RunAsync();

            Assert.IsType<NotFoundException>(thrown);
            Assert.Equal(Messages.ModuleNotFound, thrown!.Message);
            Assert.Empty(world.Journal);
        }
    }

    [Fact]
    public async Task TurningOn_WritesOneAuditRowWithTheExactJson_TheActorAndTheTimeOfTheCall()
    {
        var world = new World(Case(ModuleCodes.Purchasing, OwnRow.Off, [], wanted: true, reason: "  Go live on Monday  "));

        var (result, thrown) = await world.RunAsync();

        Assert.Null(thrown);
        var row = Assert.Single(world.Logs.Added);
        Assert.Equal(Admin, row.Actor);
        Assert.Equal(AuditActions.ModuleEnabled, row.Action);
        Assert.Equal("SystemSetting", row.EntityType);
        Assert.Equal("Module.purchasing", row.EntityId);
        Assert.Equal("""{"Module":"purchasing","Enabled":false}""", row.Before);
        Assert.Equal("""{"Module":"purchasing","Enabled":true,"Reason":"Go live on Monday","RolesRegainingGrants":2}""", row.After);
        Assert.Equal(Now, row.At);
        Assert.Equal((ModuleCodes.Purchasing, "Purchasing", true, 2), (result!.Code, result.Name, result.Enabled, result.RolesRegainingGrants));
    }

    [Fact]
    public async Task TurningOff_WritesOneAuditRowWithTheExactJson_AndNoRolesRegaining()
    {
        var world = new World(Case(ModuleCodes.Sales, OwnRow.On, [], wanted: false, reason: "Not selling this quarter"));

        var (result, thrown) = await world.RunAsync();

        Assert.Null(thrown);
        var row = Assert.Single(world.Logs.Added);
        Assert.Equal(AuditActions.ModuleDisabled, row.Action);
        Assert.Equal("Module.sales", row.EntityId);
        Assert.Equal("""{"Module":"sales","Enabled":true}""", row.Before);
        Assert.Equal("""{"Module":"sales","Enabled":false,"Reason":"Not selling this quarter","RolesRegainingGrants":0}""", row.After);
        Assert.Equal((false, 0), (result!.Enabled, result.RolesRegainingGrants));
        Assert.False(world.RowIsOn());
    }

    [Fact]
    public async Task TheReasonIsEncodedInTheAuditRowExactlyAsTheAuditTrailAlwaysDidIt()
    {
        // The default JSON encoder escapes quotes, angle brackets, apostrophes and non-ASCII letters; the row keeps its raw text, so a subscriber must write it through the same trail.
        var world = new World(Case(ModuleCodes.Purchasing, OwnRow.Off, [], wanted: true, reason: " Owner's call <urgent> \"now\" é "));

        var (_, thrown) = await world.RunAsync();

        Assert.Null(thrown);
        Assert.Equal("""{"Module":"purchasing","Enabled":true,"Reason":"Owner@u0027s call @u003Curgent@u003E @u0022now@u0022 @u00E9","RolesRegainingGrants":2}""".Replace('@', (char)92), Assert.Single(world.Logs.Added).After);
    }

    [Fact]
    public async Task ARowThatIsNotExactlyTrue_CountsAsOff_SoTurningOffIsAnUnchangedNoOp_AndTurningOnReplacesIt()
    {
        var off = new World(Case(ModuleCodes.Sales, OwnRow.Junk, [], wanted: false, reason: null));
        var on = new World(Case(ModuleCodes.Sales, OwnRow.Junk, [], wanted: true, reason: "Good reason"));

        var (offResult, offThrown) = await off.RunAsync();
        var (onResult, onThrown) = await on.RunAsync();

        Assert.Null(offThrown);
        Assert.False(offResult!.Enabled);
        Assert.Empty(off.Logs.Added);
        Assert.Null(onThrown);
        Assert.True(onResult!.Enabled);
        Assert.Equal("""{"Module":"sales","Enabled":false}""", Assert.Single(on.Logs.Added).Before);
        Assert.True(on.RowIsOn());
    }

    [Fact]
    public async Task NoRow_IsCreatedOnlyByTurningOn_WithTheKeyAndValueTheSettingsTableHolds()
    {
        var world = new World(Case(ModuleCodes.Purchasing, OwnRow.Absent, [], wanted: true, reason: "Good reason"));

        await world.RunAsync();

        var added = Assert.Single(world.Settings.Added);
        Assert.Equal(("Module.purchasing", "true"), (added.Key, added.Value));
        Assert.Equal(["lock modules", "read row", "read others", "ready purchasing", "count roles", $"audit {AuditActions.ModuleEnabled}", "save", "invalidate"], world.Journal);
    }

    [Fact]
    public async Task AnUnchangedRequest_NeedsNoReason_ReadsNothingElse_WritesNothing_AndStillDropsTheCachedSwitches()
    {
        var world = new World(Case(ModuleCodes.Purchasing, OwnRow.On, [], wanted: true, reason: null, notReady: true));

        var (result, thrown) = await world.RunAsync();

        Assert.Null(thrown);
        Assert.True(result!.Enabled);
        Assert.Equal(0, result.RolesRegainingGrants);
        Assert.Equal(["lock modules", "read row", "invalidate"], world.Journal);
        Assert.Empty(world.Logs.Added);
        Assert.Empty(world.Settings.Added);
    }

    [Fact]
    public async Task TheFirstCheckThatRefuses_Wins_AndTheChecksAfterItAreNotAsked()
    {
        var checks = new (string? Refusal, int Blockers)[] { ("The first check says no.", 7), ("The second check says no.", 9) };
        var on = new World(Case(ModuleCodes.Purchasing, OwnRow.Off, [], wanted: true), checks: checks);
        var off = new World(Case(ModuleCodes.Purchasing, OwnRow.On, [], wanted: false), checks: checks);

        var (_, notReady) = await on.RunAsync();
        var (_, openData) = await off.RunAsync();

        Assert.IsType<ConflictException>(notReady);
        Assert.Equal("The first check says no.", notReady!.Message);
        Assert.Equal(["lock modules", "read row", "read others", "ready purchasing"], on.Journal);
        Assert.IsType<ConflictException>(openData);
        Assert.Equal(Messages.ModuleHasOpenData("Purchasing", "7 things are still open"), openData!.Message);
        Assert.Equal(["lock modules", "read row", "read others", "orphan purchasing"], off.Journal);
    }

    [Fact]
    public async Task AnAcceptingCheckIsFollowedByTheNextOne_InTheOrderRegistered_AndChecksOfOtherModulesAreNeverAsked()
    {
        var checks = new (string? Refusal, int Blockers)[] { (null, 0), (null, 0), ("The third check says no.", 2) };
        var on = new World(Case(ModuleCodes.Purchasing, OwnRow.Off, [], wanted: true), checks: checks);
        var off = new World(Case(ModuleCodes.Purchasing, OwnRow.On, [], wanted: false), checks: checks);

        var (_, notReady) = await on.RunAsync();
        var (_, openData) = await off.RunAsync();

        Assert.Equal("The third check says no.", notReady!.Message);
        Assert.Equal(["lock modules", "read row", "read others", "ready purchasing", "ready purchasing 2", "ready purchasing 3"], on.Journal);
        Assert.Equal(Messages.ModuleHasOpenData("Purchasing", "2 things are still open"), openData!.Message);
        Assert.Equal(["lock modules", "read row", "read others", "orphan purchasing", "orphan purchasing 2", "orphan purchasing 3"], off.Journal);
    }

    [Fact]
    public async Task ARefusal_ThrowsBeforeTheCacheIsDropped_SoNothingIsInvalidated()
    {
        var world = new World(Case(ModuleCodes.Returns, OwnRow.Off, [], wanted: true, reason: "Good reason"));

        var (_, thrown) = await world.RunAsync();

        Assert.IsType<ConflictException>(thrown);
        Assert.DoesNotContain(World.Invalidate, world.Journal);
        Assert.DoesNotContain(World.Save, world.Journal);
    }

    // ----- the world -----

    private static ModuleSwitchTable.Case Case(string code, OwnRow row, string[] othersOn, bool wanted, string? reason = "Good reason", bool notReady = false, bool openData = false) =>
        new(code, row, othersOn, wanted, reason, notReady, openData);

    /// <summary>
    /// One request: the settings rows, the roles, the checks, the lock, the unit of work and the audit trail, with every step written down in one journal. The handler is built by asking its constructor for each
    /// parameter (so it does not matter which ones it has), and a service it asks for that the world does not hold fails loudly.
    /// </summary>
    private sealed class World
    {
        public const string Lock = "lock modules", ReadRow = "read row", ReadOthers = "read others", CountRoles = "count roles", Save = "save", Invalidate = "invalidate";

        private readonly ModuleSwitchTable.Case _case;
        private readonly string _code;
        private readonly List<object> _services = [];

        public List<string> Journal { get; } = [];
        public Rows<SystemSetting> Settings { get; }
        public Logs Logs { get; }
        private readonly SystemSetting? _ownRow;

        /// <param name="checks">The checks of the module in the order registered (refusal when turning on, blockers when turning off); by default one, as the case says.</param>
        public World(ModuleSwitchTable.Case c, string? code = null, (string? Refusal, int Blockers)[]? checks = null)
        {
            _case = c;
            _code = code ?? c.Code;

            var rows = new List<SystemSetting> { SystemSetting.Create("Module.legacy", "true"), SystemSetting.Create(SettingKeys.AllowNegativeStock, "true") }; // a module the catalog does not know and a plain setting: never counted
            rows.AddRange(c.OthersOn.Select(o => SystemSetting.Create(SettingKeys.ForModule(o), "true")));
            _ownRow = c.Row == OwnRow.Absent ? null : SystemSetting.Create(SettingKeys.ForModule(c.Code), c.Row switch { OwnRow.On => "true", OwnRow.Off => "false", _ => "TRUE" });
            if (_ownRow is not null) rows.Add(_ownRow);

            Settings = new Rows<SystemSetting>(rows, Journal, firstNote: ReadRow, listNote: ReadOthers);
            var roles = new Rows<Role>([], Journal, countNote: CountRoles, count: 2);
            Logs = new Logs(Journal);

            var own = checks ?? [(c.NotReady ? ModuleSwitchTable.ReadinessSentence : null, c.OpenData ? ModuleSwitchTable.OpenDataCount : 0)];
            var all = new List<Check>();
            foreach (var module in ModuleSwitchTable.Codes) // a check for every module: the ones of the other modules must never be asked
                if (module == c.Code) all.AddRange(own.Select((o, i) => new Check(module, Journal, i == 0 ? "" : $" {i + 1}", o.Refusal, o.Blockers)));
                else all.Add(new Check(module, Journal, "", "must never be asked", 99));

            _services.AddRange([Settings, roles, new Switches(Journal), new Locker(Journal), new Work(this), new Caller(), new FakeClock(), Logs, .. all]);
            _services.Add(new AuditTrail(Logs, new Caller(), new FakeClock(), NullLogger<AuditTrail>.Instance));
        }

        /// <summary>What the stand-in unit of work finds when it saves or ends: the rows tracked, and the after-commit work queued by the events.</summary>
        public IEnumerable<object> Tracked => Settings.Tracked;

        public bool RowIsOn() => (Settings.Added.FirstOrDefault(s => s.Key == SettingKeys.ForModule(_case.Code)) ?? _ownRow) is { } row && SettingValues.IsTrue(row.Value);

        public async Task<(ModuleSwitchResult? Result, Exception? Thrown)> RunAsync()
        {
            var handler = (SwitchModuleHandler)Build(typeof(SwitchModuleHandler));
            try
            {
                var result = await handler.HandleAsync(new SwitchModuleCommand(_code, new ModuleSwitchRequest(_case.Wanted, _case.Reason!)), CancellationToken.None);
                return (result, null);
            }
            catch (Exception e) when (e is DomainException or ConflictException or NotFoundException)
            {
                return (null, e);
            }
        }

        /// <summary>Which outcome an answer is, told from the exception type and the shape of its sentence (not from the table's expectation).</summary>
        public SwitchOutcome OutcomeOf(ModuleSwitchResult? result, Exception? thrown) => thrown switch
        {
            null => Logs.Added.Count == 0 ? SwitchOutcome.Unchanged : SwitchOutcome.Switched,
            DomainException => SwitchOutcome.ReasonRequired,
            _ when thrown.Message == ModuleSwitchTable.ReadinessSentence => SwitchOutcome.NotReady,
            _ when thrown.Message.EndsWith("Finish or cancel them first.") => SwitchOutcome.HasOpenData,
            _ when thrown.Message.Contains("cannot be turned on until") => SwitchOutcome.NeedsOtherModules,
            _ when thrown.Message.Contains("cannot be turned off while") => SwitchOutcome.NeededByOtherModules,
            _ => throw new InvalidOperationException($"an answer the table does not know: {thrown.Message}"),
        };

        /// <summary>Builds a type from its one public constructor, giving each parameter the service of the world that fits it; a parameter of <c>IEnumerable&lt;T&gt;</c> gets every service that is a <c>T</c>.</summary>
        public object Build(Type type)
        {
            var constructor = type.GetConstructors().Single();
            var args = constructor.GetParameters().Select(p => Resolve(p.ParameterType, type)).ToArray();
            return constructor.Invoke(args);
        }

        private object Resolve(Type wanted, Type forType)
        {
            if (wanted.IsGenericType && wanted.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var element = wanted.GetGenericArguments()[0];
                var matching = _services.Where(element.IsInstanceOfType).ToList();
                var array = Array.CreateInstance(element, matching.Count);
                for (var i = 0; i < matching.Count; i++) array.SetValue(matching[i], i);
                return array;
            }
            return _services.FirstOrDefault(wanted.IsInstanceOfType) ?? throw new InvalidOperationException($"{forType.Name} asks for {wanted.Name}, which the test world does not hold");
        }

        /// <summary>The handlers of an event that the Application assembly declares, built from the world (none is built until an event needs it).</summary>
        public IEnumerable<object> HandlersOf(Type handlerInterface, Type eventType)
        {
            var contract = handlerInterface.MakeGenericType(eventType);
            return typeof(SwitchModuleHandler).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t)).Select(Build);
        }
    }

    /// <summary>What the real unit of work does, for the part a handler can see: at a save the events of the tracked roots go to their in-transaction handlers, oldest first, and the after-commit handlers are queued; the queue runs when the outermost transaction ends.</summary>
    private sealed class Work(World world) : IUnitOfWork
    {
        private readonly List<Func<Task>> _afterCommit = [];

        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            var raised = world.Tracked.OfType<IAggregateRoot>().SelectMany(r => r.RaisedEvents).OrderBy(e => e.Sequence).ToList();
            foreach (var root in world.Tracked.OfType<IAggregateRoot>()) root.ClearDomainEvents();
            foreach (var (_, domainEvent) in raised)
            {
                foreach (var handler in world.HandlersOf(typeof(IDomainEventHandler<>), domainEvent.GetType())) await Invoke(handler, typeof(IDomainEventHandler<>), domainEvent, ct);
                foreach (var handler in world.HandlersOf(typeof(IAfterCommitHandler<>), domainEvent.GetType())) _afterCommit.Add(() => Invoke(handler, typeof(IAfterCommitHandler<>), domainEvent, CancellationToken.None));
            }
            world.Journal.Add(World.Save);
            return 1;
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
        {
            var result = await work();
            var queued = _afterCommit.ToList();
            _afterCommit.Clear();
            foreach (var run in queued) await run();
            return result;
        }

        private static Task Invoke(object handler, Type contract, IDomainEvent domainEvent, CancellationToken ct) =>
            (Task)contract.MakeGenericType(domainEvent.GetType()).GetMethod("HandleAsync")!.Invoke(handler, [domainEvent, ct])!;
    }

    private sealed class Rows<T>(IEnumerable<T> rows, List<string> journal, string? firstNote = null, string? listNote = null, string? countNote = null, int count = 0) : IRepository<T> where T : class
    {
        private readonly List<T> _rows = rows.ToList();

        public List<T> Added { get; } = [];

        public IEnumerable<T> Tracked => _rows.Concat(Added);

        public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
        {
            if (firstNote is not null) journal.Add(firstNote);
            return Task.FromResult(specification.Evaluate(_rows).FirstOrDefault());
        }

        public Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
        {
            if (listNote is not null) journal.Add(listNote);
            return Task.FromResult(specification.Evaluate(_rows).ToList());
        }

        public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
        {
            if (countNote is not null) journal.Add(countNote);
            return Task.FromResult(count);
        }

        public void Add(T entity) => Added.Add(entity);
        public void AddRange(IEnumerable<T> entities) => Added.AddRange(entities);
        public void Remove(T entity) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<T> entities) => throw new NotSupportedException();
        public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<T>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
        public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T?> SingleOrDefaultAsync(ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> specification) => throw new NotSupportedException();
    }

    /// <summary>The audit rows the real <see cref="AuditTrail"/> stages; each one is noted in the journal at the moment it is staged.</summary>
    private sealed class Logs(List<string> journal) : IRepository<AuditLog>
    {
        public List<AuditLog> Added { get; } = [];

        public void Add(AuditLog entity)
        {
            Added.Add(entity);
            journal.Add($"audit {entity.Action}");
        }

        public void AddRange(IEnumerable<AuditLog> entities) => throw new NotSupportedException();
        public void Remove(AuditLog entity) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<AuditLog> entities) => throw new NotSupportedException();
        public Task<AuditLog?> FirstOrDefaultAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<AuditLog>> ListAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<AuditLog>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<TResult>> ListAsync<TResult>(ISpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditLog?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
        public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditLog?> SingleOrDefaultAsync(ISingleResultSpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<AuditLog> AsAsyncEnumerable(ISpecification<AuditLog> specification) => throw new NotSupportedException();
    }

    /// <summary>One module's two checks in one: <paramref name="refusal"/> when asked about turning on, <paramref name="blockers"/> when asked about turning off.</summary>
    private sealed class Check(string moduleCode, List<string> journal, string suffix, string? refusal, int blockers) : IModuleEnableCheck, IModuleDisableCheck
    {
        public string ModuleCode => moduleCode;

        public Task<string?> RefusalAsync(CancellationToken ct)
        {
            journal.Add($"ready {moduleCode}{suffix}");
            return Task.FromResult(refusal);
        }

        public Task<int> CountBlockersAsync(CancellationToken ct)
        {
            journal.Add($"orphan {moduleCode}{suffix}");
            return Task.FromResult(blockers);
        }

        public string Describe(int count) => $"{count} things are still open";
    }

    private sealed class Switches(List<string> journal) : IModuleSwitches
    {
        public Task<bool> IsEnabledAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> EnabledAsync(CancellationToken ct) => throw new NotSupportedException();
        public void Invalidate() => journal.Add(World.Invalidate);
    }

    private sealed class Locker(List<string> journal) : ITransactionLocker
    {
        public Task LockModulesAsync(CancellationToken ct)
        {
            journal.Add(World.Lock);
            return Task.CompletedTask;
        }

        public Task LockStockAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct) => throw new NotSupportedException();
        public Task LockDocumentSequenceAsync(string prefix, int yearMonth, int warehouseId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockRolesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task LockModulesSharedAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task LockPurchaseOrderAsync(int orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockSalesOrderAsync(int orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockReturnOriginAsync(int documentId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Caller : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => Admin;
        public string Role => throw new NotSupportedException();
        public IReadOnlyCollection<int> WarehouseIds => throw new NotSupportedException();
        public bool CanAccessWarehouse(int warehouseId) => throw new NotSupportedException();
        public bool AllWarehouses => true;
        public ActorRights Rights() => throw new NotSupportedException();
    }
}
