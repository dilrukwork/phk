using System.Collections.Generic;

namespace AzureResourceLister.Models;

public class Application
{
    public int Id { get; set; }

    /// <summary>Name of the application.</summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>Budget allocated to this application.</summary>
    public decimal Budget { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public ICollection<Resource> Resources { get; set; } = [];
}
