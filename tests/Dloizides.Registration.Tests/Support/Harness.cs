using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Persistence;
using Dloizides.Registration.Screening;
using Dloizides.Registration.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.Tests.Support;

internal sealed class AttemptsDbContext(DbContextOptions<AttemptsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyRegistrationAttempts();
}

internal sealed class RaceGateInterceptor(Barrier gate) : SaveChangesInterceptor
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!gate.SignalAndWait(MaxWait, cancellationToken))
            throw new TimeoutException("The second insert never reached the race gate.");
        return ValueTask.FromResult(result);
    }
}

internal sealed class Harness : IDisposable
{
    public const string Phrase = "Blue7HorseTrail";

    private const int Racers = 2;

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"reg-{Guid.NewGuid():N}.db");
    private readonly List<IRegistrationScreen> _hostScreens = [];
    private readonly List<IRegistrationResultEnricher> _enrichers = [];
    private SharedRegistrationScreen? _sharedScreen;
    private DuplicateNoticeThrottle? _noticeThrottle;
    private Barrier? _raceGate;

    public Harness(Action<RegistrationOptions>? configure = null)
    {
        Options.AllowedRealms = ["shop"];
        Options.AllowedVerifyHosts["shop"] = ["app.example.test"];
        configure?.Invoke(Options);
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public RegistrationOptions Options { get; } = new();

    public TestClock Clock { get; } = new();

    public FakeTenants Tenants { get; } = new();

    public FakeIdentity Identity { get; } = new();

    public FakeVerification Verification { get; } = new();

    public CapturingLogger<RegisterAccount> RegisterLog { get; } = new();

    public CapturingLogger<RegistrationReconciler> ReconcileLog { get; } = new();

    public CapturingLogger<AccountEraser> EraserLog { get; } = new();

    public List<string> Callbacks { get; } = [];

    public static RegistrationRequest Request(string email = "ana@example.test", string? key = "key-1") => new()
    {
        Realm = "shop",
        Email = email,
        Username = email,
        Password = Phrase,
        TenantName = "Ana Co",
        VerifyUrlTemplate = "https://app.example.test/verify?token={token}",
        IdempotencyKey = key,
    };

    public void AddScreen(IRegistrationScreen screen) => _hostScreens.Add(screen);

    public void AddEnricher(IRegistrationResultEnricher enricher) => _enrichers.Add(enricher);

    public void ForceBeginRace() => _raceGate = new Barrier(Racers);

    public async Task<RegistrationResponse> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = NewContext();
        var useCase = new RegisterAccount(
            Steps(db),
            [_sharedScreen ??= new SharedRegistrationScreen(Wrap(), Clock), .. _hostScreens],
            _enrichers,
            Verification,
            Wrap(),
            RegisterLog,
            _noticeThrottle ??= new DuplicateNoticeThrottle(Wrap(), Clock));
        return await useCase.ExecuteAsync(request, cancellationToken);
    }

    public async Task<ReconcileSummary> ReconcileAsync()
    {
        await using var db = NewContext();
        return await new RegistrationReconciler(Steps(db), Verification, Wrap(), ReconcileLog).RunOnceAsync(CancellationToken.None);
    }

    public async Task<RegistrationAttempt?> BeginAsync(RegistrationAttempt attempt)
    {
        await using var db = NewContext();
        return await new RegistrationAttemptStore<AttemptsDbContext>(db).TryBeginAsync(attempt, CancellationToken.None);
    }

    public async Task<AccountEraseResult> EraseAsync(AccountEraseRequest request, Func<CancellationToken, Task<bool>> participantsConfirmed)
    {
        await using var db = NewContext();
        var callbacks = new AccountEraseCallbacks(
            _ => RecordAsync("confirm"),
            _ => RecordAsync("announce"),
            participantsConfirmed);
        return await Eraser(db).EraseAsync(request, callbacks, CancellationToken.None);
    }

    public async Task<AccountEraseResult> EraseAsync(AccountEraseRequest request)
    {
        await using var db = NewContext();
        var callbacks = new AccountEraseCallbacks(
            _ => RecordAsync("confirm"),
            _ => RecordAsync("announce"));
        return await Eraser(db).EraseAsync(request, callbacks, CancellationToken.None);
    }

    public async Task<AccountRestoreOutcome> RestoreAsync(AccountRestoreRequest request)
    {
        await using var db = NewContext();
        return await Eraser(db).RestoreAsync(request, CancellationToken.None);
    }

    public async Task<AccountEraseResult> CloseByOperatorAsync(AccountEraseRequest request, string operatorId, OperatorCloseMode mode = OperatorCloseMode.LoginAbsentOnly)
    {
        await using var db = NewContext();
        return await Eraser(db).CloseByOperatorAsync(request, new OperatorCloseRequest(operatorId, mode), _ => RecordAsync("announce"), CancellationToken.None);
    }

    public async Task<T> GateAsync<T>(Func<NewPasswordGate, Task<T>> use)
    {
        await using var db = NewContext();
        return await use(new NewPasswordGate(Identity, new RegistrationAttemptStore<AttemptsDbContext>(db), Wrap()));
    }

    public async Task SeedAttemptAsync(RegistrationAttemptState state, string tenantId, string email = "ana@example.test", string? userId = null)
    {
        await using var db = NewContext();
        var now = Clock.GetUtcNow().UtcDateTime;
        db.Set<RegistrationAttempt>().Add(new RegistrationAttempt
        {
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            Realm = "shop",
            Username = email,
            Email = email,
            TenantId = tenantId,
            UserId = userId,
            VerifyUrlTemplate = "https://app.example.test/verify?token={token}",
            State = state,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<RegistrationAttempt>> AttemptsAsync()
    {
        await using var db = NewContext();
        return await db.Set<RegistrationAttempt>().AsNoTracking().ToListAsync();
    }

    public void ClearFailures()
    {
        Tenants.BeforeCreate = Tenants.AfterCreate = Tenants.OnRemove = null;
        Identity.BeforeCreate = Identity.BeforeRoles = Identity.AfterRoles = Identity.OnLookup = null;
        Verification.OnSend = null;
    }

    public void Dispose()
    {
        _raceGate?.Dispose();
        File.Delete(_file);
    }

    private Task RecordAsync(string callback)
    {
        Callbacks.Add($"{callback}:{Identity.Users.Count}");
        return Task.CompletedTask;
    }

    private AccountEraser Eraser(AttemptsDbContext db) =>
        new(Tenants, Identity, new RegistrationAttemptStore<AttemptsDbContext>(db), Wrap(), Clock, EraserLog);

    private IOptions<RegistrationOptions> Wrap() => Microsoft.Extensions.Options.Options.Create(Options);

    private RegistrationSteps Steps(AttemptsDbContext db) =>
        new(Tenants, Identity, new RegistrationAttemptStore<AttemptsDbContext>(db), Clock);

    private AttemptsDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<AttemptsDbContext>().UseSqlite($"Data Source={_file};Pooling=False");
        if (_raceGate is not null)
            builder.AddInterceptors(new RaceGateInterceptor(_raceGate));
        return new AttemptsDbContext(builder.Options);
    }
}
