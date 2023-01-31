namespace QSI
{
    public class THMessage
    {
        public int messageId { get; set; }
        public string deviceId { get; set; }
        public double temperature { get; set; }
        public double humidity { get; set; }
    }
}