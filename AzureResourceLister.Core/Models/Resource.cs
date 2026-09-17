namespace AzureResourceLister.Models;

public class Resource
{
    public int Id { get; set; }

    /// <summary>Full ARM resource ID from Azure (e.g. /subscriptions/{sub}/resourceGroups/{rg}/...).</summary>
    public string AzureResourceId { get; set; } = string.Empty;

    public int SubscriptionId { get; set; }

    public string ResourceName { get; set; } = string.Empty;

    public string ResourceGroup { get; set; } = string.Empty;

    /// <summary>Cost centre code, sourced from the CostCentre tag on the resource or its resource group.</summary>
    public string CostCentre { get; set; } = string.Empty;

    /// <summary>
    /// "24x7" or "5x12" — derived from the dxcAutoShutdownSchedule tag at import time.
    /// Used by the Savings Plan hour-by-hour simulation to know which hours this resource
    /// is actually running (and therefore drawing from the shared hourly commitment).
    /// Defaults to "24x7" when the tag is missing, disabled, or unrecognised.
    /// </summary>
    public string HoursOfOperation { get; set; } = "24x7";

    /// <summary>true = current/active, false = deleted.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// true = manually flagged to never appear in Savings Plan calculations — e.g. a
    /// resource known to be decommissioned soon that shouldn't factor into a 1/3-year
    /// commitment decision. Set/unset from the Savings Plan page; persists indefinitely.
    /// </summary>
    public bool ExcludeFromSavingsPlan { get; set; }

    /// <summary>"AzureSync" (normal Resources refresh) or "CostSheet" (reconstructed from a billing row whose resource no longer exists in Azure).</summary>
    public string Source { get; set; } = "AzureSync";

    // ── Navigation ────────────────────────────────────────────────────────────
    public Subscription Subscription { get; set; } = null!;

    /// <summary>Applications using this resource — one row for a dedicated resource, many for a shared one.</summary>
    public ICollection<ResourceApp> ResourceApps { get; set; } = [];

    /// <summary>Owners associated with this resource.</summary>
    public ICollection<ResourceOwner> ResourceOwners { get; set; } = [];
}
