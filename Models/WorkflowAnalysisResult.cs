using System.Text.Json.Serialization;

namespace WorkflowCreator.Models
{
    /// <summary>
    /// Result of AI-powered workflow analysis containing structured workflow information.
    /// This model represents the output from the cloud AI analysis phase.
    /// </summary>
    public class WorkflowAnalysisResult
    {
        /// <summary>
        /// Indicates whether the analysis was successful.
        /// If false, check ErrorMessage for details.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if analysis failed.
        /// Null if Success is true.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// AI-extracted workflow name suitable for business systems.
        /// Example: "Purchase Order Approval", "Employee Onboarding"
        /// </summary>
        public string? WorkflowName { get; set; }

        /// <summary>
        /// Structured list of workflow steps extracted and analyzed by AI.
        /// Ordered by the Order property of each step.
        /// </summary>
        public List<WorkflowStep>? Steps { get; set; }

        /// <summary>
        /// Workflow transitions extracted by AI analysis showing flow logic.
        /// Contains the complete flow between steps via trigger statuses.
        /// </summary>
        public List<WorkflowTransition>? FlowTransitions { get; set; }

        /// <summary>
        /// Statuses that are required for this workflow but don't exist in the system.
        /// These need to be manually added to the PAWSActivityStatus table.
        /// </summary>
        public List<WorkflowStatus>? RequiredStatuses { get; set; }

        /// <summary>
        /// Existing statuses in the system that the workflow will use.
        /// These are mapped from the workflow requirements to current PAWS statuses.
        /// </summary>
        public List<WorkflowStatus>? ExistingStatuses { get; set; }

        /// <summary>
        /// Additional metadata about the analysis process.
        /// Includes timing, AI provider info, caching status, etc.
        /// </summary>
        public Dictionary<string, object>? AnalysisMetadata { get; set; }

        /// <summary>
        /// Gets all statuses (required + existing) for the workflow.
        /// Useful for displaying complete status information.
        /// </summary>
        [JsonIgnore]
        public List<WorkflowStatus> AllStatuses
        {
            get
            {
                var all = new List<WorkflowStatus>();
                if (RequiredStatuses != null) all.AddRange(RequiredStatuses);
                if (ExistingStatuses != null) all.AddRange(ExistingStatuses);
                return all;
            }
        }

        /// <summary>
        /// Gets the count of new statuses that need to be created.
        /// </summary>
        [JsonIgnore]
        public int NewStatusCount => RequiredStatuses?.Count ?? 0;

        /// <summary>
        /// Gets the count of existing statuses that will be reused.
        /// </summary>
        [JsonIgnore]
        public int ExistingStatusCount => ExistingStatuses?.Count ?? 0;

        /// <summary>
        /// Gets the total number of workflow steps identified.
        /// </summary>
        [JsonIgnore]
        public int StepCount => Steps?.Count ?? 0;

        /// <summary>
        /// Gets whether this result came from cache or fresh analysis.
        /// </summary>
        [JsonIgnore]
        public bool WasCached => AnalysisMetadata?.ContainsKey("CachedResult") == true &&
                                (bool)(AnalysisMetadata["CachedResult"]);

        /// <summary>
        /// Gets the analysis time in milliseconds.
        /// </summary>
        [JsonIgnore]
        public long AnalysisTimeMs => AnalysisMetadata?.ContainsKey("AnalysisTimeMs") == true ?
                                    Convert.ToInt64(AnalysisMetadata["AnalysisTimeMs"]) : 0;

        /// <summary>
        /// Gets the AI provider used for analysis.
        /// </summary>
        [JsonIgnore]
        public string AIProvider => AnalysisMetadata?.ContainsKey("AIProvider") == true ?
                                  AnalysisMetadata["AIProvider"].ToString() ?? "Unknown" : "Unknown";

        /// <summary>
        /// Validates that the analysis result has all required components.
        /// </summary>
        /// <returns>List of validation issues, empty if valid</returns>
        public List<string> Validate()
        {
            var issues = new List<string>();

            if (!Success)
            {
                if (string.IsNullOrEmpty(ErrorMessage))
                    issues.Add("Failed analysis must have an error message");
                return issues; // Don't validate other fields if failed
            }

            if (string.IsNullOrWhiteSpace(WorkflowName))
                issues.Add("Workflow name is required");

            if (Steps == null || !Steps.Any())
                issues.Add("At least one workflow step is required");
            else
            {
                // Validate steps have proper ordering
                var expectedOrder = 1;
                foreach (var step in Steps.OrderBy(s => s.Order))
                {
                    if (step.Order != expectedOrder)
                        issues.Add($"Step ordering gap: expected {expectedOrder}, found {step.Order}");
                    expectedOrder++;
                }
            }

            if (AllStatuses.Count == 0)
                issues.Add("At least one status is required for the workflow");

            return issues;
        }

        /// <summary>
        /// Creates a summary string of the analysis for logging or display.
        /// </summary>
        /// <returns>Human-readable summary</returns>
        public string GetSummary()
        {
            if (!Success)
                return $"Analysis failed: {ErrorMessage}";

            return $"'{WorkflowName}' - {StepCount} steps, {ExistingStatusCount} existing statuses, {NewStatusCount} new statuses needed";
        }

    }
}
