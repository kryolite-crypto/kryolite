using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace Kryolite.Module.Mdns;

public class DnsPacket
{
    public ushort Id;

    // QR, OPCODE, AA, TC, RD, RA, Z, RCODE
    public ushort Flags;

    public ushort QDCount;

    public ushort ANCount;

    public ushort NSCount = 0;

    public ushort ARCount = 0;

    public Question[] Questions = [];
    public Answer[] Answers = [];

    public bool QR
    {
        get => (Flags & (1 << 15)) != 0;
        set => Flags |= 1 << 15;
    }

    public bool AA
    {
        get => (Flags & (1 << 10)) != 0;
        set => Flags |= 1 << 10;
    }

    public bool TC
    {
        get => (Flags & (1 << 9)) != 0;
        set => Flags |= 1 << 9;
    }

    public bool RD
    {
        get => (Flags & (1 << 8)) != 0;
        set => Flags |= 1 << 8;
    }

    public bool RA
    {
        get => (Flags & (1 << 7)) != 0;
        set => Flags |= 1 << 7;
    }

    public override string ToString()
    {
        var text = $"""
        Header
            Id = {Id}
            QR = {QR}
            AA = {AA}
            TC = {TC}
            RD = {RD}
            RA = {RA}
            QDCount = {QDCount}
            ANCount = {ANCount}
        """;

        text += Environment.NewLine + "Questions";

        foreach (var question in Questions)
        {
            text += Environment.NewLine + $"""
                {question.QName}
                    QType = {question.QType}
                    QClass = {question.QClass}
                    UR = {question.UR}
            """;
        }

        text += Environment.NewLine + "Answers";

        foreach (var answer in Answers)
        {
            text += Environment.NewLine + $"""
                {answer.Name}
                    {answer.Type}
                    {answer.Class}
                    {answer.TTL}
                    {answer.RDLength}
            """;
        }

        return text;
    }

    public byte[] ToArray()
    {
        List<byte> bytes = [];

        bytes.AddRange(BitConverter.GetBytes(Id).Reverse());
        bytes.AddRange(BitConverter.GetBytes(Flags).Reverse());
        bytes.AddRange(BitConverter.GetBytes(QDCount).Reverse());
        bytes.AddRange(BitConverter.GetBytes(ANCount).Reverse());
        bytes.AddRange(BitConverter.GetBytes(NSCount).Reverse());
        bytes.AddRange(BitConverter.GetBytes(ARCount).Reverse());

        foreach (var question in Questions)
        {
            bytes.AddRange(question.ToArray());
        }

        foreach (var answer in Answers)
        {
            bytes.AddRange(answer.ToArray());
        }

        return [.. bytes];
    }

    public void AddAnswer(string name, ushort type, IRecord record)
    {
        var data = record.ToArray();
        var answer = new Answer
        {
            Name = name,
            Type = type,
            Class = 1,
            TTL = 0,
            RDLength = (ushort)data.Length,
            RData = data
        };

        Answers = [..Answers, answer];
        ANCount = (ushort)Answers.Length;
    }

    public void AddQuestion(string name, ushort type)
    {
        var question = new Question
        {
            QName = name,
            QType = type,
            QClass = 1
        };

        Questions = [..Questions, question];
        QDCount = (ushort)Questions.Length;
    }

    public void AddSRV(string hostname, string serviceName, ushort port)
    {
        var record = new SRVRecord
        {
            Priority = 0,
            Weight = 0,
            Port = port,
            Target = hostname
        };

        AddAnswer($"{hostname}.{serviceName}", SRVRecord.TYPE, record);
    }

    public void AddTXT(string hostname, string serviceName)
    {
        var record = new TXTRecord
        {
            Text = "txtvers=1"
        };

        AddAnswer($"{hostname}.{serviceName}", TXTRecord.TYPE, record);
    }

    public void AddPTR(string hostname, string serviceName)
    {
        var record = new PTRRecord
        {
            Target = $"{hostname}.{serviceName}"
        };

        AddAnswer(serviceName, PTRRecord.TYPE, record);
    }

    public void AddA(string hostname, IPAddress localAddress)
    {
        var record = new ARecord
        {
            Target = localAddress
        };

        AddAnswer(hostname, ARecord.TYPE, record);
    }

    public static bool TryParse(byte[] data, [NotNullWhen(true)] out DnsPacket? dnsPacket)
    {
        if (data.Length < 12)
        {
            dnsPacket = null;
            return false;
        }

        dnsPacket = new DnsPacket
        {
            Id = BitConverter.ToUInt16(data[0..2].Reverse().ToArray()),
            Flags = BitConverter.ToUInt16(data[2..4].Reverse().ToArray()),
            QDCount = BitConverter.ToUInt16(data[4..6].Reverse().ToArray()),
            ANCount = BitConverter.ToUInt16(data[6..8].Reverse().ToArray()),
            NSCount = BitConverter.ToUInt16(data[8..10].Reverse().ToArray()),
            ARCount = BitConverter.ToUInt16(data[10..12].Reverse().ToArray())
        };

        dnsPacket.Questions = new Question[dnsPacket.QDCount];

        var offset = 12;

        for (var i = 0; i < dnsPacket.Questions.Length; i++)
        {
            if (!Question.TryParse(data, ref offset, out var question))
            {
                return false;
            }
    
            dnsPacket.Questions[i] = question;
        }

        dnsPacket.Answers = new Answer[dnsPacket.ANCount];

        for (var i = 0; i < dnsPacket.Answers.Length; i++)
        {
            if (!Answer.TryParse(data, ref offset, out var answer))
            {
                return false;
            }

            dnsPacket.Answers[i] = answer;
        }

        return true;
    }
}