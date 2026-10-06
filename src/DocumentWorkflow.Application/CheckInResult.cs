namespace DocumentWorkflow.Application;

public sealed record CheckoutConflict(int BaseVersion, int CurrentVersion, string WorkingPath, bool HasLocalEdits);
public sealed record CheckInResult(bool Succeeded, CheckoutConflict? Conflict = null, string? Message = null);
