namespace WorkflowCreator.Services
{
    public class OpenAIConfig
    {
        public string ApiKey { get; set; } = "";
        public string ModelId { get; set; } = "gpt-4o-mini";
        public string OrganizationId { get; set; } = "";
    }
}
