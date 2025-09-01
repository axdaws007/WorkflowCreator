namespace WorkflowCreator.Models
{
    public class WorkflowTransition
    {
        public string? SourceStep { get; set; }           // NULL for workflow start
        public string TriggerStatus { get; set; } = "";   // User action name
        public string? DestinationStep { get; set; }      // NULL for termination
        public bool IsProgressive { get; set; }           // true = forward, false = backward

    }
}
