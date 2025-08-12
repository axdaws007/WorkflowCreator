using System.Text.RegularExpressions;
using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    public class WorkflowService : IWorkflowService
    {
        private static List<WorkflowModel> _workflows = new List<WorkflowModel>();
        private static int _nextId = 1;

        public WorkflowResultViewModel ProcessWorkflowDescription(string description)
        {
            try
            {
                // Parse the natural language description
                var steps = ParseWorkflowSteps(description);

                // Create workflow model
                var workflow = new WorkflowModel
                {
                    Id = _nextId++,
                    Name = ExtractWorkflowName(description),
                    Description = description,
                    CreatedAt = DateTime.Now,
                    Status = "Processing"
                };

                _workflows.Add(workflow);

                return new WorkflowResultViewModel
                {
                    Success = true,
                    Message = "Workflow created successfully! Generating SQL...",
                    Workflow = workflow,
                    Steps = steps
                };
            }
            catch (Exception ex)
            {
                return new WorkflowResultViewModel
                {
                    Success = false,
                    Message = $"Error processing workflow: {ex.Message}"
                };
            }
        }

        public List<WorkflowModel> GetAllWorkflows()
        {
            return _workflows;
        }

        private List<string> ParseWorkflowSteps(string description)
        {
            var steps = new List<string>();

            // Look for keywords that indicate steps
            var keywords = new[] { "then", "next", "after", "finally", "first", "second", "third", "when", "if", "once" };
            var sentences = description.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    // Check for numbered steps
                    if (Regex.IsMatch(trimmed, @"^\d+[\.\)]\s*"))
                    {
                        steps.Add(Regex.Replace(trimmed, @"^\d+[\.\)]\s*", "").Trim());
                    }
                    // Check for bullet points
                    else if (trimmed.StartsWith("-") || trimmed.StartsWith("*") || trimmed.StartsWith("•"))
                    {
                        steps.Add(Regex.Replace(trimmed, @"^[-*•]\s*", "").Trim());
                    }
                    // Check for keyword indicators
                    else if (keywords.Any(k => trimmed.ToLower().Contains(k)))
                    {
                        steps.Add(trimmed);
                    }
                    // If no clear structure, treat each sentence as a potential step
                    else if (steps.Count == 0 || sentences.Length <= 3)
                    {
                        steps.Add(trimmed);
                    }
                }
            }

            return steps.Count > 0 ? steps : new List<string> { description };
        }

        private string ExtractWorkflowName(string description)
        {
            // Try to extract a meaningful name from the description
            var words = description.Split(' ').Take(5);
            var name = string.Join(" ", words);

            if (name.Length > 50)
                name = name.Substring(0, 47) + "...";

            return name;
        }
    }
}
