namespace WorkflowCreator.Models
{
    public class OllamaRequest
    {
        public string model { get; set; } = "llama2";
        public string prompt { get; set; } = "";
        public bool stream { get; set; } = false;
        public OllamaOptions? options { get; set; }
    }
}
