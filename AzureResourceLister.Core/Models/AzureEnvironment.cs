using System.Collections.Generic;

namespace AzureResourceLister.Models;

/// <summary>
/// Represents a deployment environment (e.g. Production, Staging, Dev).
/// Named AzureEnvironment to avoid collision with System.Environment.
/// </summary>
public class AzureEnvironment
{
    public int Id { get; set; }

    public string EnvironmentName { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
