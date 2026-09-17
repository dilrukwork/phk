using System.Collections.Generic;

namespace AzureResourceLister.Models;

public class BusinessOwner
{
    public int Id { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────
    public ICollection<Resource> Resources { get; set; } = [];
}
