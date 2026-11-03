# Dloizides.Registration

One registration and account-erase flow, hosted inside each service on that service's own database. Product rules plug in through five ports.

## Ports

| Port | Step |
|---|---|
| `IRegistrationScreen` | Extra request screening; the shared screen always runs |
| `ITenantProvisioner` | Create, remove, disable and restore the tenant |
| `IIdentityProvisioner` | Create, find, disable and delete the login, in the request's realm |
| `IVerificationChannel` | Send the verification message |
| `IRegistrationResultEnricher` | Add product fields to the first response |

## Host wiring

```csharp
services.AddRegistration<IdentityDbContext>(options => configuration.GetSection(RegistrationOptions.SectionName).Bind(options));
modelBuilder.ApplyRegistrationAttempts();
jobsBuilder.AddJob<RegistrationReconcileJob>();
```

Call `RegisterAccount.ExecuteAsync` from the endpoint and map `RegistrationResponse` to HTTP. Call `AccountEraser.EraseAsync` from the sweep with the realm stored on the deletion request; store `RecordLoginConfirmed` and pass it back as `LoginPreviouslyConfirmed`; close the request only when `MayComplete` is true. `DeletionGracePolicy.Resolve` gives the grace for a new deletion request.

## Host duties

- Alert on the Critical log for `LoginNotFound` and `TenantMismatch`: the deletion request stays open and nothing was erased. After an operator has checked it, call `AccountEraser.CloseByOperatorAsync`. A login that is gone closes with an announce. A found login is refused unless the operator picks `OperatorCloseMode.DeleteFoundLogin` and `Email` matches the login; that writes an audit line, disables and deletes the login, then announces. No tenant is removed.
- Pass the account's `Username` and `Email` on `AccountEraseRequest`, stored with the deletion request, so every attempt row for the person is deleted, also on resume.
- Before signing a verified user in, call `NewPasswordGate.IsRequiredAsync`. When true, issue no session and do not mark the address verified until `SetAsync` returns `Done`.
- Ports honour their cancellation token: `StepTimeout` relies on it.
