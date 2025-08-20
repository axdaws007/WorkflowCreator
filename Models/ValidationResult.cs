namespace WorkflowCreator.Models
{
    /// <summary>
    /// Result of configuration validation containing any errors or warnings found.
    /// Used during application startup to validate AI service configurations and dependencies.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// List of non-critical warnings about the configuration.
        /// Application can still function but may have reduced capabilities.
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// List of critical errors that prevent proper application function.
        /// Application should not start or will have severe limitations.
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Gets whether the configuration is valid (no critical errors).
        /// Warnings are acceptable, but errors indicate serious issues.
        /// </summary>
        public bool IsValid => !Errors.Any();

        /// <summary>
        /// Gets whether the configuration has any issues (errors or warnings).
        /// </summary>
        public bool HasIssues => Errors.Any() || Warnings.Any();

        /// <summary>
        /// Gets the total count of issues found.
        /// </summary>
        public int IssueCount => Errors.Count + Warnings.Count;

        /// <summary>
        /// Adds a warning to the validation result.
        /// </summary>
        /// <param name="warning">Warning message to add</param>
        public void AddWarning(string warning)
        {
            if (!string.IsNullOrWhiteSpace(warning))
                Warnings.Add(warning);
        }

        /// <summary>
        /// Adds an error to the validation result.
        /// </summary>
        /// <param name="error">Error message to add</param>
        public void AddError(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
                Errors.Add(error);
        }

        /// <summary>
        /// Merges another validation result into this one.
        /// </summary>
        /// <param name="other">Other validation result to merge</param>
        public void Merge(ValidationResult other)
        {
            if (other != null)
            {
                Warnings.AddRange(other.Warnings);
                Errors.AddRange(other.Errors);
            }
        }

        /// <summary>
        /// Creates a summary string of all issues found.
        /// </summary>
        /// <returns>Human-readable summary of validation issues</returns>
        public string GetSummary()
        {
            if (IsValid && !Warnings.Any())
                return "Configuration is valid with no issues";

            var parts = new List<string>();

            if (Errors.Any())
                parts.Add($"{Errors.Count} error{(Errors.Count > 1 ? "s" : "")}");

            if (Warnings.Any())
                parts.Add($"{Warnings.Count} warning{(Warnings.Count > 1 ? "s" : "")}");

            return $"Configuration has {string.Join(" and ", parts)}";
        }
    }
}
