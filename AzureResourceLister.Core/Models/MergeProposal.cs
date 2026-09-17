using System;

namespace AzureResourceLister.Models;

/// <summary>
/// Represents an AI-suggested merge of a duplicate Application or BusinessOwner row
/// into a canonical row. Nothing is changed in Application/BusinessOwner/Resource
/// until a proposal is reviewed (IsApproved = true) and then applied (IsApplied = true).
///
/// Review workflow:
///   SELECT * FROM MergeProposal WHERE IsApplied = 0 ORDER BY EntityType, TargetName;
///   UPDATE MergeProposal SET IsApproved = 1 WHERE Id IN (...);
///   -- then run menu option "Apply Approved Merges"
/// </summary>
public class MergeProposal
{
    public int Id { get; set; }

    /// <summary>"Application" or "BusinessOwner".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The duplicate row to be merged away (its FK references move to TargetId, then it's deleted).</summary>
    public int SourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;

    /// <summary>The canonical row that survives.</summary>
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;

    /// <summary>"High", "Medium", or "Low" — as judged by the AI.</summary>
    public string Confidence { get; set; } = string.Empty;

    /// <summary>Short AI-generated explanation for why these were grouped.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>Set to true by the reviewer (via SQL) before the merge is applied.</summary>
    public bool IsApproved { get; set; }

    /// <summary>Set to true once the merge has actually been performed.</summary>
    public bool IsApplied { get; set; }

    public DateTime  CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAt { get; set; }
}
