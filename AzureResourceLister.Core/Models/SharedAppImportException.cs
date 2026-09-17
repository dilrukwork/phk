using System;

namespace AzureResourceLister.Models;

/// <summary>
/// A row from the shared-app registry CSV whose Azure resource (matched by
/// ResourceName + ResourceGroup + SubscriptionName) couldn't be found.
/// Application/Owner are not the problem here — they're created automatically if
/// missing, since this data comes from an authoritative system, not noisy tags.
/// </summary>
public class SharedAppImportException
{
    public int Id { get; set; }

    public string ApplicationName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string AzureResourceName { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;

    /// <summary>Why it didn't resolve, e.g. "Resource not found" or "Subscription not found".</summary>
    public string Reason { get; set; } = string.Empty;

    public string SourceFile { get; set; } = string.Empty;
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
}
