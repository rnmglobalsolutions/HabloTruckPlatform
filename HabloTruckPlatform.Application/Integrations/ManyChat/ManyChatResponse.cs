
namespace HabloTruckPlatform.Application.Integrations.ManyChat
{
    public class Details
    {
        public List<Message> messages { get; set; } = new List<Message>();
    }

    public class Message
    {
        public string? message { get; set; }
    }

    public class ManyChatResponse
    {
        public string? status { get; set; }
        public string? message { get; set; }
        public Details? details { get; set; }
    }
}
