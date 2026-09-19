using System.Threading.Tasks;

namespace AzureResourceLister.Llm;

/// <summary>
/// Abstraction over an LLM backend used for data cleansing prompts.
/// Implementations: AnthropicLlmProvider (cloud) and OllamaLlmProvider (fully local).
/// </summary>
public interface ILlmProvider
{
    /// <summary>Sends a single prompt and returns the raw text response.</summary>
    Task<string> CompleteAsync(string prompt);

    /// <summary>Human-readable description of the active provider, for console logging.</summary>
    string Description { get; }
}
