using WorkflowCreator.Models;

namespace WorkflowCreator.Services
{
    public interface IWorkflowService
    {
        WorkflowResultViewModel ProcessWorkflowDescription(string description);
        List<WorkflowModel> GetAllWorkflows();
    }
}
