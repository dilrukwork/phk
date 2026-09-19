using System.Collections.Generic;

namespace AzureResourceLister.Models;

public class Subscription
{
    public int Id { get; set; }

    public string SubscriptionName { get; set; } = string.Empty;

    /// <summary>The Azure subscription GUID (e.g. xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).</summary>
    public string AzureSubscriptionId { get; set; } = string.Empty;

    public int EnvironmentId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public AzureEnvironment Environment { get; set; } = null!;
    public ICollection<Resource> Resources { get; set; } = [];
}
