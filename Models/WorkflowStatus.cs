using System.Text.Json.Serialization;

namespace WorkflowCreator.Models
{
    /// <summary>
    /// Represents a workflow status that is either existing in the system or needs to be created.
    /// Used for mapping workflow requirements to the PAWSActivityStatus table.
    /// </summary>
    public class WorkflowStatus
    {
        /// <summary>
        /// Name of the status (e.g., "Approved", "Pending Review", "Rejected").
        /// Should match or be suitable for the PAWSActivityStatus.title column.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Description of when and how this status is used in the workflow.
        /// Helps with understanding the business logic.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Whether this status already exists in the PAWSActivityStatus table.
        /// If true, use ExistingId. If false, this status needs to be manually created.
        /// </summary>
        public bool IsExisting { get; set; }

        /// <summary>
        /// The ActivityStatusID from PAWSActivityStatus table if this status exists.
        /// Null if IsExisting is false.
        /// </summary>
        public int? ExistingId { get; set; }

        /// <summary>
        /// Suggested ID for new statuses (not yet in database).
        /// Used for planning new status creation.
        /// </summary>
        public int? SuggestedId { get; set; }

        /// <summary>
        /// Category of status for grouping purposes.
        /// Examples: "Approval", "Processing", "Completion", "Error"
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Whether this status represents a final state in the workflow.
        /// Terminal statuses don't have outgoing transitions.
        /// </summary>
        public bool IsTerminal { get; set; }

        /// <summary>
        /// Whether this status represents an error or rejection state.
        /// </summary>
        public bool IsErrorState { get; set; }

        /// <summary>
        /// Priority or ordering hint for status display.
        /// Lower numbers appear first in lists.
        /// </summary>
        public int DisplayOrder { get; set; }

        /// <summary>
        /// Additional metadata about the status from AI analysis.
        /// May include confidence scores, alternative names, etc.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Gets the ID to use in SQL generation (existing or suggested).
        /// </summary>
        [JsonIgnore]
        public int? UsableId => IsExisting ? ExistingId : SuggestedId;

        /// <summary>
        /// Gets a display name with status type indicator.
        /// </summary>
        [JsonIgnore]
        public string DisplayName => IsExisting ? $"{Name} (Existing)" : $"{Name} (New)";

        /// <summary>
        /// Validates the workflow status data.
        /// </summary>
        /// <returns>List of validation issues</returns>
        public List<string> Validate()
        {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(Name))
                issues.Add("Status name is required");

            if (string.IsNullOrWhiteSpace(Description))
                issues.Add("Status description is required");

            if (IsExisting && !ExistingId.HasValue)
                issues.Add("Existing status must have an ExistingId");

            if (!IsExisting && !SuggestedId.HasValue)
                issues.Add("New status should have a SuggestedId");

            if (Name.Length > 100)
                issues.Add("Status name should be 100 characters or less");

            return issues;
        }

        /// <summary>
        /// Creates a summary string for the status.
        /// </summary>
        /// <returns>Formatted status summary</returns>
        public override string ToString()
        {
            var summary = $"{Name}";
            if (IsExisting)
                summary += $" (ID: {ExistingId})";
            else
                summary += " (New)";

            if (IsTerminal)
                summary += " [Final]";

            return summary;
        }
    }
}
