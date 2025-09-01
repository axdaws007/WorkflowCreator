using System.Text.Json.Serialization;

namespace WorkflowCreator.Models
{
    /// <summary>
    /// Represents a single step in a workflow as analyzed by AI.
    /// Contains structured information about what happens in the step.
    /// </summary>
    public class WorkflowStep
    {
        /// <summary>
        /// Sequential order of this step in the workflow (starting from 1).
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Concise title of the step (e.g., "Manager Review", "Submit Request").
        /// Should be 3-6 words suitable for UI display.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Detailed description of what happens in this step.
        /// Usually 1-2 sentences explaining the process.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Estimated duration for this step (if provided in description).
        /// Not always available from AI analysis.
        /// </summary>
        public TimeSpan? EstimatedDuration { get; set; }

        /// <summary>
        /// Whether this step can be performed by multiple people simultaneously.
        /// Useful for parallel approval processes.
        /// </summary>
        public bool AllowsParallelExecution { get; set; }

        /// <summary>
        /// Priority level of this step (High, Medium, Low).
        /// Inferred from workflow description or set to Medium by default.
        /// </summary>
        public WorkflowStepPriority Priority { get; set; } = WorkflowStepPriority.Medium;

        /// <summary>
        /// Additional metadata about the step extracted during AI analysis.
        /// May include confidence scores, alternative interpretations, etc.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Validates the workflow step data.
        /// </summary>
        /// <returns>List of validation issues</returns>
        public List<string> Validate()
        {
            var issues = new List<string>();

            if (Order <= 0)
                issues.Add("Step order must be greater than 0");

            if (string.IsNullOrWhiteSpace(Title))
                issues.Add("Step title is required");

            if (string.IsNullOrWhiteSpace(Description))
                issues.Add("Step description is required");

            if (Title.Length > 100)
                issues.Add("Step title should be 100 characters or less");

            if (Description.Length > 500)
                issues.Add("Step description should be 500 characters or less");

            return issues;
        }

        /// <summary>
        /// Creates a display-friendly summary of the step.
        /// </summary>
        /// <returns>Formatted step summary</returns>
        public override string ToString()
        {
            var summary = $"{Order}. {Title}";
            return summary;
        }
    }

    /// <summary>
    /// Priority levels for workflow steps.
    /// </summary>
    public enum WorkflowStepPriority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }
}
