using Dloizides.Registration.Model;
using Microsoft.EntityFrameworkCore;

namespace Dloizides.Registration.Persistence;

public static class RegistrationAttemptMapping
{
    internal const int MaxNameLength = 256;
    internal const int MaxUrlLength = 2048;

    private const int MaxKeyLength = 128;

    /// <summary>Maps the additive registration_attempts table into the host's DbContext.</summary>
    public static ModelBuilder ApplyRegistrationAttempts(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RegistrationAttempt>(entity =>
        {
            entity.ToTable("registration_attempts");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.IdempotencyKey).IsRequired().HasMaxLength(MaxKeyLength);
            entity.Property(a => a.Realm).IsRequired().HasMaxLength(MaxNameLength);
            entity.Property(a => a.Username).IsRequired().HasMaxLength(MaxNameLength);
            entity.Property(a => a.Email).IsRequired().HasMaxLength(MaxNameLength);
            entity.Property(a => a.TenantId).IsRequired().HasMaxLength(MaxNameLength);
            entity.Property(a => a.UserId).HasMaxLength(MaxNameLength);
            entity.Property(a => a.VerifyUrlTemplate).HasMaxLength(MaxUrlLength);
            entity.HasIndex(a => new { a.Realm, a.IdempotencyKey }).IsUnique();
            entity.HasIndex(a => new { a.State, a.UpdatedAt });
        });
        return modelBuilder;
    }
}
