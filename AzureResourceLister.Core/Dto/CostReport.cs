namespace AzureResourceLister.Dto;

/// <summary>
/// Generic report result. Environments is the ordered list of active environment names —
/// Razor pages iterate this to render one cost column per environment.
/// </summary>
public class CostReport<T>
{
    /// <summary>Ordered environment names for column headers. Only environments with
    /// non-zero cost in this period are included.</summary>
    public List<string> Environments { get; set; } = [];

    public List<T> Rows { get; set; } = [];

    public decimal GrandTotal { get; set; }
}
