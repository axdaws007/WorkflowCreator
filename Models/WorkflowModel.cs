namespace WorkflowCreator.Models
{
    public class WorkflowModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Status { get; set; }
        public string? GeneratedSql { get; set; }
    }
}
