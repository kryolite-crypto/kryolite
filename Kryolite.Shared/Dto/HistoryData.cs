namespace Kryolite.Shared;

public class HistoryData
{
    public List<TimeData> Difficulty { get; set; } = [];
    public List<TimeData> Weight { get; set; } = [];
    public List<TimeData> TxPerSecond { get; set; } = [];
    public List<TimeData> TotalWork { get; set; } = [];
    public List<TimeData> WeightPerView { get; set; } = [];
    public List<TimeData> TotalTransactions { get; set; } = [];
}

public class TimeData
{
    public long X { get; set; }
    public double Y { get; set; }
}
